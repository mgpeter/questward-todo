using System.Net;
using System.Net.Http.Json;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Isolation;

/// <summary>
/// The point of the whole spec: two accounts on one instance must not be able to see or
/// touch each other's data through any path.
/// </summary>
/// <remarks>
/// A missed <c>UserId</c> filter does not throw or fail loudly. It leaks data, or lets one
/// user's activity unlock another's badges. These tests are the only thing standing
/// between a one-line omission and that outcome.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class UserIsolationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private QuestwardAppFactory _factory = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _factory = new QuestwardAppFactory(postgres.ConnectionString);
        _alice = _factory.CreateClientAs("auth0|alice", "alice@example.com", "Alice");
        _bob = _factory.CreateClientAs("auth0|bob", "bob@example.com", "Bob");
    }

    public ValueTask DisposeAsync()
    {
        _alice.Dispose();
        _bob.Dispose();
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<TaskDto> CreateTaskAsync(HttpClient client, string title, string difficulty)
    {
        var response = await client.PostAsJsonAsync("/api/tasks", new { title, difficulty });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
    }

    [Fact]
    public async Task One_users_tasks_never_appear_in_anothers_list()
    {
        await CreateTaskAsync(_alice, "Alice's secret plan", "medium");
        await CreateTaskAsync(_alice, "Alice's other plan", "easy");

        var bobsTasks = await _bob.GetFromJsonAsync<List<TaskDto>>("/api/tasks");

        Assert.Empty(bobsTasks!);

        var alicesTasks = await _alice.GetFromJsonAsync<List<TaskDto>>("/api/tasks");
        Assert.Equal(2, alicesTasks!.Count);
    }

    [Fact]
    public async Task Filters_and_search_do_not_leak_across_users()
    {
        await CreateTaskAsync(_alice, "Alice's findable thing", "epic");

        Assert.Empty((await _bob.GetFromJsonAsync<List<TaskDto>>("/api/tasks?status=open"))!);
        Assert.Empty((await _bob.GetFromJsonAsync<List<TaskDto>>("/api/tasks?status=all"))!);
        Assert.Empty((await _bob.GetFromJsonAsync<List<TaskDto>>("/api/tasks?difficulty=epic"))!);
        Assert.Empty((await _bob.GetFromJsonAsync<List<TaskDto>>("/api/tasks?search=findable"))!);
    }

    [Fact]
    public async Task Another_users_task_is_indistinguishable_from_one_that_does_not_exist()
    {
        var task = await CreateTaskAsync(_alice, "Alice's task", "medium");

        // 404 rather than 403, so ids cannot be probed for existence.
        Assert.Equal(HttpStatusCode.NotFound, (await _bob.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _bob.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/tasks/{task.Id}/complete", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/tasks/{task.Id}/reopen", null)).StatusCode);

        var update = await _bob.PutAsJsonAsync(
            $"/api/tasks/{task.Id}",
            new { title = "Hijacked", difficulty = "easy", priority = "normal" });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        // And the task is untouched.
        var stillThere = await _alice.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}");
        Assert.Equal("Alice's task", stillThere!.Title);
    }

    [Fact]
    public async Task Reorder_cannot_touch_another_users_tasks()
    {
        var aliceTask = await CreateTaskAsync(_alice, "Alice's task", "medium");
        var bobTask = await CreateTaskAsync(_bob, "Bob's task", "medium");

        // Bob submits Alice's id alongside his own.
        var response = await _bob.PostAsJsonAsync(
            "/api/tasks/reorder",
            new { orderedIds = new[] { aliceTask.Id, bobTask.Id } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var aliceAfter = await _alice.GetFromJsonAsync<TaskDto>($"/api/tasks/{aliceTask.Id}");
        Assert.Equal(aliceTask.SortOrder, aliceAfter!.SortOrder);
    }

    [Fact]
    public async Task XP_is_awarded_only_to_the_user_who_did_the_work()
    {
        var task = await CreateTaskAsync(_alice, "Alice's epic", "epic");

        var completion = await _alice.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/complete",
            new { utcOffsetMinutes = 0 });
        completion.EnsureSuccessStatusCode();

        var alice = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");
        var bob = await _bob.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.Equal(100, alice!.TotalXp);
        Assert.Equal(2, alice.Level);
        Assert.Equal(1, alice.TasksCompleted);

        Assert.Equal(0, bob!.TotalXp);
        Assert.Equal(1, bob.Level);
        Assert.Equal(0, bob.TasksCompleted);
    }

    [Fact]
    public async Task Badges_unlock_per_user_not_per_instance()
    {
        // First Blood is the clearest case: with a unique index on AchievementKey alone,
        // Alice earning it would permanently prevent Bob from ever earning it.
        var aliceTask = await CreateTaskAsync(_alice, "Alice's first", "medium");
        await _alice.PostAsJsonAsync($"/api/tasks/{aliceTask.Id}/complete", new { utcOffsetMinutes = 0 });

        var bobBefore = await _bob.GetFromJsonAsync<List<AchievementDto>>("/api/achievements");
        Assert.DoesNotContain(bobBefore!, a => a.Unlocked);

        var bobTask = await CreateTaskAsync(_bob, "Bob's first", "medium");
        var bobCompletion = await _bob.PostAsJsonAsync(
            $"/api/tasks/{bobTask.Id}/complete",
            new { utcOffsetMinutes = 0 });
        bobCompletion.EnsureSuccessStatusCode();

        var result = await bobCompletion.Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.Contains(result!.UnlockedAchievements, a => a.Key == "first-blood");

        var bobAfter = await _bob.GetFromJsonAsync<List<AchievementDto>>("/api/achievements");
        Assert.Contains(bobAfter!, a => a is { Key: "first-blood", Unlocked: true });
    }

    [Fact]
    public async Task Clean_Slate_looks_at_the_callers_board_not_the_instance()
    {
        // Bob keeps tasks open throughout. If the open-task counts were unscoped, Alice
        // clearing her own board would never register as clean.
        await CreateTaskAsync(_bob, "Bob leaves this open", "easy");
        await CreateTaskAsync(_bob, "And this one", "easy");

        var alicesTasks = new List<TaskDto>
        {
            await CreateTaskAsync(_alice, "Alice one", "easy"),
            await CreateTaskAsync(_alice, "Alice two", "easy"),
            await CreateTaskAsync(_alice, "Alice three", "easy")
        };

        HttpResponseMessage? last = null;

        foreach (var task in alicesTasks)
        {
            last = await _alice.PostAsJsonAsync(
                $"/api/tasks/{task.Id}/complete",
                new { utcOffsetMinutes = 0 });
        }

        var result = await last!.Content.ReadFromJsonAsync<CompleteResponse>();

        Assert.Contains(result!.UnlockedAchievements, a => a.Key == "clean-slate");
    }

    [Fact]
    public async Task Giant_Killer_counts_only_the_callers_hard_tasks()
    {
        // Bob grinds out nine Hard tasks. Alice's first Hard task must not be treated as
        // her tenth just because the instance has ten in total.
        for (var i = 0; i < 9; i++)
        {
            var bobTask = await CreateTaskAsync(_bob, $"Bob hard {i}", "hard");
            await _bob.PostAsJsonAsync($"/api/tasks/{bobTask.Id}/complete", new { utcOffsetMinutes = 0 });
        }

        var aliceTask = await CreateTaskAsync(_alice, "Alice hard", "hard");
        var completion = await _alice.PostAsJsonAsync(
            $"/api/tasks/{aliceTask.Id}/complete",
            new { utcOffsetMinutes = 0 });

        var result = await completion.Content.ReadFromJsonAsync<CompleteResponse>();

        Assert.DoesNotContain(result!.UnlockedAchievements, a => a.Key == "giant-killer");
    }

    [Fact]
    public async Task Stats_report_only_the_callers_work()
    {
        await CreateTaskAsync(_bob, "Bob's task", "epic");
        var aliceTask = await CreateTaskAsync(_alice, "Alice's task", "medium");
        await _alice.PostAsJsonAsync($"/api/tasks/{aliceTask.Id}/complete", new { utcOffsetMinutes = 0 });

        var stats = await _alice.GetFromJsonAsync<StatsDto>("/api/stats?utcOffsetMinutes=0");

        Assert.Equal(1, stats!.TotalTasks);
        Assert.Equal(1, stats.CompletedTasks);
        Assert.Equal(0, stats.OpenTasks);
        Assert.Equal(25, stats.TotalXp);

        // Bob's Epic must not show up in Alice's breakdown.
        Assert.Equal(0, stats.ByDifficulty.Single(b => b.Difficulty == "epic").Completed);
        Assert.Equal(1, stats.ByDifficulty.Single(b => b.Difficulty == "medium").Completed);
    }

    [Fact]
    public async Task Renaming_a_character_does_not_rename_anyone_elses()
    {
        var updated = await _alice.PutAsJsonAsync(
            "/api/character",
            new { name = "Alicia", avatarKey = "owl" });
        updated.EnsureSuccessStatusCode();

        var bob = await _bob.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.Equal("Bob", bob!.Name);
        Assert.Equal("fox", bob.AvatarKey);
    }

    [Fact]
    public async Task The_full_XP_flow_still_behaves_per_user()
    {
        // Phase 0 regression: idempotent completion, exact refunds, badges never revoked,
        // now asserted while a second user is active on the same instance.
        await CreateTaskAsync(_bob, "Bob noise", "epic");

        var task = await CreateTaskAsync(_alice, "Alice's hard task", "hard");

        var first = await (await _alice.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/complete", new { utcOffsetMinutes = 0 }))
            .Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.Equal(50, first!.XpGained);

        var again = await (await _alice.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/complete", new { utcOffsetMinutes = 0 }))
            .Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.Equal(0, again!.XpGained);
        Assert.Equal(50, again.Character.TotalXp);

        var reopened = await (await _alice.PostAsync($"/api/tasks/{task.Id}/reopen", null))
            .Content.ReadFromJsonAsync<ReopenResponse>();
        Assert.Equal(50, reopened!.XpLost);
        Assert.Equal(0, reopened.Character.TotalXp);

        // Badges survive the reopen.
        var achievements = await _alice.GetFromJsonAsync<List<AchievementDto>>("/api/achievements");
        Assert.Contains(achievements!, a => a is { Key: "deep-work", Unlocked: true });

        // Bob was never touched.
        var bob = await _bob.GetFromJsonAsync<CharacterDto>("/api/character");
        Assert.Equal(0, bob!.TotalXp);
    }

    [Fact]
    public async Task Another_users_gear_cannot_be_broken_down_or_reforged()
    {
        // Three new routes that each take an item id and spend a currency. A missing UserId
        // filter here does not throw: it lets one account destroy another's inventory.
        await ChooseClassAsync(_alice);
        await ChooseClassAsync(_bob);

        var alices = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var target = alices![0].Id;

        foreach (var verb in new[] { "salvage", "imbue", "reforge" })
        {
            // 404 rather than 403, so ids cannot be probed for existence.
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await _bob.PostAsync($"/api/rpg/inventory/{target}/{verb}", null)).StatusCode);
        }

        var stillThere = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        Assert.Contains(stillThere!, i => i.Id == target);
    }

    [Fact]
    public async Task Essence_is_earned_only_by_the_account_that_broke_the_item()
    {
        // Essence is a balance on the character row, so an unscoped credit would be the same
        // class of bug as XP leaking between accounts.
        await ChooseClassAsync(_alice);
        await ChooseClassAsync(_bob);

        var alices = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var scrap = alices![0].Id;

        await _alice.PostAsync($"/api/rpg/inventory/{scrap}/unequip", null);
        (await _alice.PostAsync($"/api/rpg/inventory/{scrap}/salvage", null)).EnsureSuccessStatusCode();

        Assert.True((await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet"))!.Essence > 0);
        Assert.Equal(0, (await _bob.GetFromJsonAsync<SheetDto>("/api/rpg/sheet"))!.Essence);

        // And Bob's own gear survived Alice's visit to the forge.
        Assert.Equal(2, (await _bob.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory"))!.Count);
    }

    [Fact]
    public async Task One_adventurers_bestiary_is_invisible_to_another()
    {
        // The codex is a per-user chronicle. An unscoped read would hand Bob a page saying he
        // had met a monster he has never fought, and an unscoped write would credit Alice's
        // kills to him.
        await ChooseClassAsync(_alice);
        await ChooseClassAsync(_bob);
        await GrantStaminaAsync(_alice);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });
        start.EnsureSuccessStatusCode();
        var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();

        for (var round = 0; round < 30; round++)
        {
            var attack = await _alice.PostAsync($"/api/rpg/encounters/{encounter!.Id}/attack", null);
            if (!attack.IsSuccessStatusCode) break;

            var result = await attack.Content.ReadFromJsonAsync<AttackDto>();
            if (result!.Encounter.Status != "active") break;
        }

        var alices = await _alice.GetFromJsonAsync<BestiaryDto>("/api/rpg/bestiary");
        var bobs = await _bob.GetFromJsonAsync<BestiaryDto>("/api/rpg/bestiary");

        Assert.Equal(1, alices!.Discovered);
        Assert.Equal(1, alices.Slain);

        // Bob sees the same catalog and none of the history.
        Assert.Equal(alices.Total, bobs!.Total);
        Assert.Equal(0, bobs.Discovered);
        Assert.Equal(0, bobs.Slain);
        Assert.All(bobs.Entries, e =>
        {
            Assert.False(e.IsDiscovered);
            Assert.Null(e.Blurb);
            Assert.Equal(0, e.Encounters);
            Assert.Equal(0, e.Kills);
            Assert.Equal(0, e.GoldTaken);
        });
    }

    [Fact]
    public async Task One_adventurers_lore_is_invisible_to_another()
    {
        // Lore is derived from the bestiary rather than stored, so a leak here would be a
        // leak in the derivation rather than in a table, and no foreign key would catch it.
        await ChooseClassAsync(_alice);
        await ChooseClassAsync(_bob);
        await GrantStaminaAsync(_alice);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });
        start.EnsureSuccessStatusCode();
        var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();
        (await _alice.PostAsync($"/api/rpg/encounters/{encounter!.Id}/flee", null))
            .EnsureSuccessStatusCode();

        var alices = await _alice.GetFromJsonAsync<LoreDto>("/api/rpg/lore");
        var bobs = await _bob.GetFromJsonAsync<LoreDto>("/api/rpg/lore");

        var alicesFragment = alices!.Places
            .SelectMany(p => p.Fragments)
            .Single(f => f.Key == "giant-rat-sighted");
        var bobsFragment = bobs!.Places
            .SelectMany(p => p.Fragments)
            .Single(f => f.Key == "giant-rat-sighted");

        Assert.True(alicesFragment.IsUnlocked);
        Assert.NotNull(alicesFragment.Body);

        Assert.False(bobsFragment.IsUnlocked);
        Assert.Null(bobsFragment.Body);
        Assert.Equal(alices.Total, bobs.Total);
        Assert.True(bobs.Unlocked < alices.Unlocked);
    }

    [Fact]
    public async Task Discovering_a_monster_advances_only_the_discoverers_quest()
    {
        await ChooseClassAsync(_alice);
        await ChooseClassAsync(_bob);
        await GrantStaminaAsync(_alice);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });
        start.EnsureSuccessStatusCode();
        var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();
        (await _alice.PostAsync($"/api/rpg/encounters/{encounter!.Id}/flee", null))
            .EnsureSuccessStatusCode();

        var alicesQuests = await _alice.GetFromJsonAsync<List<QuestDto>>("/api/rpg/quests");
        var bobsQuests = await _bob.GetFromJsonAsync<List<QuestDto>>("/api/rpg/quests");

        Assert.Equal(1, alicesQuests!.Single(q => q.Key == QuestCatalog.FieldNotes).Objectives.Single().Current);
        Assert.Equal(0, bobsQuests!.Single(q => q.Key == QuestCatalog.FieldNotes).Objectives.Single().Current);
    }

    private static async Task ChooseClassAsync(HttpClient client) =>
        (await client.PutAsJsonAsync("/api/rpg/class", new { classKey = ClassCatalog.Fighter }))
            .EnsureSuccessStatusCode();

    /// <remarks>
    /// Easy tasks deliberately. An Epic one grants more stamina but levels the character past
    /// the low monsters these tests fight, which turns a start request into a 400.
    /// </remarks>
    private static async Task GrantStaminaAsync(HttpClient client, int count = 2)
    {
        for (var i = 0; i < count; i++)
        {
            var task = await CreateTaskAsync(client, $"Stamina {i}", "easy");

            await client.PostAsJsonAsync($"/api/tasks/{task.Id}/complete", new { utcOffsetMinutes = 0 });
        }
    }

    private sealed record TaskDto(Guid Id, string Title, int SortOrder, bool IsCompleted);

    private sealed record ItemDto(Guid Id, string ItemKey, string Name, bool IsEquipped);

    private sealed record SheetDto(int Gold, int Essence);

    private sealed record CharacterDto(
        string Name,
        string AvatarKey,
        int Level,
        int TotalXp,
        int TasksCompleted);

    private sealed record AchievementDto(string Key, bool Unlocked);

    private sealed record CompleteResponse(
        int XpGained,
        CharacterDto Character,
        bool LeveledUp,
        List<AchievementDto> UnlockedAchievements);

    private sealed record ReopenResponse(int XpLost, CharacterDto Character);

    private sealed record DifficultyBreakdownDto(string Difficulty, int Completed, int XpEarned);

    private sealed record StatsDto(
        int TotalTasks,
        int OpenTasks,
        int CompletedTasks,
        int TotalXp,
        List<DifficultyBreakdownDto> ByDifficulty);

    private sealed record EncounterDto(Guid Id, string MonsterKey, string Status, int Round);

    private sealed record AttackDto(EncounterDto Encounter);

    private sealed record ObjectiveDto(string Id, int Current, int Required);

    private sealed record QuestDto(string Key, List<ObjectiveDto> Objectives);

    private sealed record BestiaryEntryDto(
        string Key,
        string Name,
        string? Blurb,
        int Level,
        bool IsDiscovered,
        bool IsSlain,
        int Encounters,
        int Kills,
        int GoldTaken,
        int BestRound,
        DateTimeOffset? FirstSeenAt,
        DateTimeOffset? LastSeenAt);

    private sealed record BestiaryDto(
        List<BestiaryEntryDto> Entries, int Discovered, int Slain, int Total);

    private sealed record LoreFragmentDto(
        string Key, string Title, string? Body, bool IsUnlocked, string Requirement);

    private sealed record LorePlaceDto(
        string Key, string Name, string Blurb, List<LoreFragmentDto> Fragments, int Unlocked, int Total);

    private sealed record LoreDto(List<LorePlaceDto> Places, int Unlocked, int Total);
}
