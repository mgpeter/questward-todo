using System.Net;
using System.Net.Http.Json;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// The two read routes Phase 4 added, exercised through the wire.
/// </summary>
/// <remarks>
/// The DTO mirrors at the bottom are hand-written on purpose, the same as every other wire
/// shape in this suite. They are what makes a silent change to <c>BestiaryEntryDto</c> or
/// <c>LoreFragmentDto</c> fail a test rather than reach a client that no longer understands it.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class BestiaryEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private QuestwardAppFactory _factory = null!;
    private HttpClient _alice = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _factory = new QuestwardAppFactory(postgres.ConnectionString);
        _alice = _factory.CreateClientAs("auth0|alice");
    }

    public ValueTask DisposeAsync()
    {
        _alice.Dispose();
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task ChooseClassAsync(HttpClient client) =>
        (await client.PutAsJsonAsync("/api/rpg/class", new { classKey = ClassCatalog.Fighter }))
            .EnsureSuccessStatusCode();

    /// <remarks>
    /// Easy tasks, following the note in <c>RpgEndpointTests</c>: an Epic task grants more
    /// stamina but levels the character straight past the monsters these tests fight.
    /// </remarks>
    private static async Task GrantStaminaAsync(HttpClient client, int count = 3)
    {
        for (var i = 0; i < count; i++)
        {
            var task = await client.PostAsJsonAsync(
                "/api/tasks", new { title = $"Stamina {i}", difficulty = "easy" });
            var created = await task.Content.ReadFromJsonAsync<TaskDto>();

            await client.PostAsJsonAsync($"/api/tasks/{created!.Id}/complete", new { utcOffsetMinutes = 0 });
        }
    }

    private static async Task<Guid> StartAsync(HttpClient client, string monsterKey)
    {
        var start = await client.PostAsJsonAsync("/api/rpg/encounters", new { monsterKey });
        start.EnsureSuccessStatusCode();

        return (await start.Content.ReadFromJsonAsync<EncounterDto>())!.Id;
    }

    private static async Task FightToTheEndAsync(HttpClient client, Guid encounterId)
    {
        for (var round = 0; round < 30; round++)
        {
            var attack = await client.PostAsync($"/api/rpg/encounters/{encounterId}/attack", null);

            if (!attack.IsSuccessStatusCode)
            {
                return;
            }

            var result = await attack.Content.ReadFromJsonAsync<AttackDto>();

            if (result!.Encounter.Status != "active")
            {
                return;
            }
        }
    }

    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("/api/rpg/bestiary")]
    [InlineData("/api/rpg/lore")]
    public async Task The_codex_routes_require_authentication(string route)
    {
        using var anonymous = _factory.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task The_bestiary_lists_the_whole_catalog_before_anything_has_been_met()
    {
        var bestiary = await _alice.GetFromJsonAsync<BestiaryDto>("/api/rpg/bestiary");

        Assert.NotNull(bestiary);
        Assert.Equal(MonsterCatalog.All.Count, bestiary.Total);
        Assert.Equal(MonsterCatalog.All.Count, bestiary.Entries.Count);
        Assert.Equal(0, bestiary.Discovered);
        Assert.Equal(0, bestiary.Slain);

        Assert.All(bestiary.Entries, e =>
        {
            Assert.False(e.IsDiscovered);
            Assert.False(e.IsSlain);

            // The description is the reward for the first sighting, so an unmet row must not
            // give it away.
            Assert.Null(e.Blurb);
            Assert.Null(e.FirstSeenAt);
            Assert.Null(e.LastSeenAt);
            Assert.Equal(0, e.Encounters);
            Assert.Equal(0, e.BestRound);

            // The catalog half of the row is always there, or there would be nothing to aim at.
            Assert.NotEmpty(e.Name);
            Assert.True(e.Level > 0);
        });

        // Ordered by level then name, so the page reads as a ladder.
        Assert.Equal(
            bestiary.Entries.OrderBy(e => e.Level).ThenBy(e => e.Name, StringComparer.Ordinal).ToList(),
            bestiary.Entries);
    }

    [Fact]
    public async Task A_first_sighting_reveals_the_row_without_claiming_a_kill()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice);

        var encounterId = await StartAsync(_alice, MonsterCatalog.Goblin);
        (await _alice.PostAsync($"/api/rpg/encounters/{encounterId}/flee", null)).EnsureSuccessStatusCode();

        var bestiary = await _alice.GetFromJsonAsync<BestiaryDto>("/api/rpg/bestiary");
        var goblin = bestiary!.Entries.Single(e => e.Key == MonsterCatalog.Goblin);

        Assert.True(goblin.IsDiscovered);
        Assert.False(goblin.IsSlain);
        Assert.Equal(1, goblin.Encounters);
        Assert.Equal(0, goblin.Kills);
        Assert.Equal(0, goblin.BestRound);
        Assert.Equal(MonsterCatalog.Find(MonsterCatalog.Goblin)!.Blurb, goblin.Blurb);
        Assert.NotNull(goblin.FirstSeenAt);

        Assert.Equal(1, bestiary.Discovered);
        Assert.Equal(0, bestiary.Slain);

        // Everything else stayed unmet.
        Assert.All(
            bestiary.Entries.Where(e => e.Key != MonsterCatalog.Goblin),
            e => Assert.False(e.IsDiscovered));
    }

    [Fact]
    public async Task A_win_shows_as_a_kill_with_its_gold_and_its_round()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice);

        await FightToTheEndAsync(_alice, await StartAsync(_alice, MonsterCatalog.GiantRat));

        var bestiary = await _alice.GetFromJsonAsync<BestiaryDto>("/api/rpg/bestiary");
        var rat = bestiary!.Entries.Single(e => e.Key == MonsterCatalog.GiantRat);

        Assert.True(rat.IsSlain);
        Assert.Equal(1, rat.Encounters);
        Assert.Equal(1, rat.Kills);
        Assert.True(rat.BestRound > 0, "a kill has to have taken at least one round");
        Assert.True(rat.GoldTaken > 0);
        Assert.Equal(1, bestiary.Slain);
    }

    // -------------------------------------------------------------------------

    [Fact]
    public async Task Lore_arrives_whole_with_its_locked_bodies_held_back()
    {
        var lore = await _alice.GetFromJsonAsync<LoreDto>("/api/rpg/lore");

        Assert.NotNull(lore);
        Assert.Equal(LoreCatalog.Places.Count, lore.Places.Count);
        Assert.Equal(LoreCatalog.All.Count, lore.Total);
        Assert.Equal(lore.Places.Sum(p => p.Fragments.Count), lore.Total);
        Assert.Equal(lore.Places.Sum(p => p.Unlocked), lore.Unlocked);

        var locked = lore.Places.SelectMany(p => p.Fragments).Where(f => !f.IsUnlocked).ToList();

        Assert.NotEmpty(locked);
        Assert.All(locked, f =>
        {
            // The body is the whole of the reward, so it is the only thing withheld. The
            // title and the requirement stay, or the page is a wall of blanks.
            Assert.Null(f.Body);
            Assert.NotEmpty(f.Title);
            Assert.NotEmpty(f.Requirement);
        });

        var ratSighted = locked.Single(f => f.Key == "giant-rat-sighted");
        Assert.Equal("Meet the Giant Rat", ratSighted.Requirement);

        var ratKnown = locked.Single(f => f.Key == "giant-rat-known");
        Assert.Equal("Defeat the Giant Rat 3 times", ratKnown.Requirement);

        Assert.Contains(locked, f => f.Requirement == "Reach level 4");
        Assert.Contains(locked, f => f.Requirement == "Claim Honest Work");
    }

    [Fact]
    public async Task Meeting_a_monster_opens_its_field_note()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice);

        var before = await _alice.GetFromJsonAsync<LoreDto>("/api/rpg/lore");

        var encounterId = await StartAsync(_alice, MonsterCatalog.GiantRat);
        (await _alice.PostAsync($"/api/rpg/encounters/{encounterId}/flee", null)).EnsureSuccessStatusCode();

        var after = await _alice.GetFromJsonAsync<LoreDto>("/api/rpg/lore");

        var fragment = after!.Places
            .SelectMany(p => p.Fragments)
            .Single(f => f.Key == "giant-rat-sighted");

        Assert.True(fragment.IsUnlocked);
        Assert.NotNull(fragment.Body);
        Assert.NotEmpty(fragment.Body);
        Assert.Equal(before!.Unlocked + 1, after.Unlocked);

        // The total never moves. It is the catalog, not a tally of what has been found.
        Assert.Equal(before.Total, after.Total);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// The guarantee the whole design rests on, asked again of the routes added this phase.
    /// </summary>
    [Fact]
    public async Task No_codex_route_can_move_experience()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice, count: 4);

        var before = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        for (var fight = 0; fight < 4; fight++)
        {
            var start = await _alice.PostAsJsonAsync(
                "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });

            if (start.StatusCode != HttpStatusCode.Created)
            {
                break;
            }

            var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();
            await FightToTheEndAsync(_alice, encounter!.Id);

            // Reading the codex is what the phase added. Doing it between fights, while the
            // counters are moving, is where an accidental award would be easiest to hide.
            (await _alice.GetAsync("/api/rpg/bestiary")).EnsureSuccessStatusCode();
            (await _alice.GetAsync("/api/rpg/lore")).EnsureSuccessStatusCode();
        }

        var after = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        // The chronicle filled up. The experience did not move.
        Assert.True((await _alice.GetFromJsonAsync<BestiaryDto>("/api/rpg/bestiary"))!.Slain > 0);
        Assert.Equal(before!.TotalXp, after!.TotalXp);
        Assert.Equal(before.Level, after.Level);
    }

    // ---- wire shapes -------------------------------------------------------

    private sealed record TaskDto(Guid Id);
    private sealed record CharacterDto(int Level, int TotalXp);
    private sealed record EncounterDto(Guid Id, string MonsterKey, string Status, int Round);
    private sealed record AttackDto(EncounterDto Encounter);

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
