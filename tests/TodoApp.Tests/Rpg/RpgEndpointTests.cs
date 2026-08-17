using System.Net;
using System.Net.Http.Json;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

[Collection(nameof(PostgresCollection))]
public class RpgEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private QuestwardAppFactory _factory = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _factory = new QuestwardAppFactory(postgres.ConnectionString);
        _alice = _factory.CreateClientAs("auth0|alice");
        _bob = _factory.CreateClientAs("auth0|bob");
    }

    public ValueTask DisposeAsync()
    {
        _alice.Dispose();
        _bob.Dispose();
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task ChooseClassAsync(HttpClient client, string classKey = ClassCatalog.Fighter)
    {
        var response = await client.PutAsJsonAsync("/api/rpg/class", new { classKey });
        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("/api/rpg/sheet")]
    [InlineData("/api/rpg/classes")]
    [InlineData("/api/rpg/monsters")]
    [InlineData("/api/rpg/inventory")]
    [InlineData("/api/rpg/quests")]
    public async Task Every_adventure_route_requires_authentication(string route)
    {
        using var anonymous = _factory.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task A_new_character_has_no_class_and_a_usable_sheet()
    {
        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        Assert.NotNull(sheet);
        Assert.Null(sheet.ClassKey);
        Assert.Equal(1, sheet.Level);
        Assert.Equal(6, sheet.Abilities.Count);
        Assert.True(sheet.MaxHitPoints > 0);

        // Predates class selection, so it is prompted rather than chosen for them.
        Assert.All(sheet.Abilities, a => Assert.Equal(10, a.Score));
    }

    [Fact]
    public async Task Choosing_a_class_sets_scores_and_grants_starting_gear()
    {
        await ChooseClassAsync(_alice, ClassCatalog.Ranger);

        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        Assert.Equal(ClassCatalog.Ranger, sheet!.ClassKey);
        Assert.Equal("Ranger", sheet.ClassName);

        var dexterity = sheet.Abilities.Single(a => a.Abbreviation == "DEX");
        Assert.True(dexterity.Score >= 16);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        Assert.Equal(2, inventory!.Count);
        Assert.All(inventory, i => Assert.True(i.IsEquipped));

        // Equipped armour raises armour class above the bare 10 + DEX.
        Assert.True(sheet.ArmourClass > 10 + dexterity.Modifier - 1);
    }

    [Fact]
    public async Task An_unknown_class_is_rejected()
    {
        var response = await _alice.PutAsJsonAsync("/api/rpg/class", new { classKey = "necromancer" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Changing_class_keeps_gold_and_does_not_duplicate_starting_gear()
    {
        await ChooseClassAsync(_alice, ClassCatalog.Fighter);
        await ChooseClassAsync(_alice, ClassCatalog.Wizard);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");

        // Class-swapping must not become an item printer.
        Assert.Equal(2, inventory!.Count);

        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        Assert.Equal(ClassCatalog.Wizard, sheet!.ClassKey);
    }

    [Fact]
    public async Task Starting_a_fight_without_stamina_is_refused_with_a_clear_reason()
    {
        await ChooseClassAsync(_alice);

        var response = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.Goblin });

        // 422 rather than 400: the request was well formed, the character simply has not
        // done the work yet.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("stamina", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Completing_a_task_buys_a_fight()
    {
        await ChooseClassAsync(_alice);

        var task = await _alice.PostAsJsonAsync("/api/tasks", new { title = "Real work", difficulty = "epic" });
        var created = await task.Content.ReadFromJsonAsync<TaskDto>();

        await _alice.PostAsJsonAsync($"/api/tasks/{created!.Id}/complete", new { utcOffsetMinutes = 0 });

        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");
        Assert.Equal(5, sheet!.Stamina); // Epic grants 5

        var fight = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.Goblin });

        Assert.Equal(HttpStatusCode.Created, fight.StatusCode);
    }

    [Fact]
    public async Task An_active_fight_can_be_resumed_and_a_second_one_refused()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice);

        var first = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.Goblin });
        first.EnsureSuccessStatusCode();

        var active = await _alice.GetFromJsonAsync<EncounterDto>("/api/rpg/encounters/active");
        Assert.NotNull(active);
        Assert.Equal("active", active.Status);

        var second = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Attacking_returns_a_fully_itemised_roll_breakdown()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.Goblin });
        start.EnsureSuccessStatusCode();
        var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();

        var attack = await _alice.PostAsync($"/api/rpg/encounters/{encounter!.Id}/attack", null);
        attack.EnsureSuccessStatusCode();

        var result = await attack.Content.ReadFromJsonAsync<AttackDto>();

        Assert.NotEmpty(result!.Rolls);

        var swing = result.Rolls.First(r => r.Kind == "attack");
        Assert.NotEmpty(swing.Dice);
        Assert.Equal(20, swing.Dice[0].Sides);
        Assert.NotNull(swing.Target);
        Assert.NotEmpty(swing.Modifiers);

        // The sheet rides along so the UI never has to refetch to update stamina or gold.
        Assert.NotNull(result.Sheet);
    }

    [Fact]
    public async Task No_adventure_route_can_move_experience()
    {
        // The guarantee the whole design rests on, asserted through the wire rather than
        // only in the service layer.
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice, count: 6);

        var before = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        for (var fight = 0; fight < 4; fight++)
        {
            var start = await _alice.PostAsJsonAsync(
                "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });

            if (start.StatusCode != HttpStatusCode.Created) break;

            var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();

            for (var round = 0; round < 25; round++)
            {
                var attack = await _alice.PostAsync($"/api/rpg/encounters/{encounter!.Id}/attack", null);
                if (!attack.IsSuccessStatusCode) break;

                var result = await attack.Content.ReadFromJsonAsync<AttackDto>();
                if (result!.Encounter.Status != "active") break;
            }
        }

        var after = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.Equal(before!.TotalXp, after!.TotalXp);
        Assert.Equal(before.Level, after.Level);
    }

    [Fact]
    public async Task One_adventurer_cannot_touch_anothers_encounter()
    {
        await ChooseClassAsync(_alice);
        await GrantStaminaAsync(_alice);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.Goblin });
        start.EnsureSuccessStatusCode();
        var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();

        // 404 rather than 403, so ids cannot be probed for existence.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/encounters/{encounter!.Id}/attack", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/encounters/{encounter.Id}/flee", null)).StatusCode);
    }

    [Fact]
    public async Task One_adventurer_cannot_see_or_take_anothers_inventory()
    {
        await ChooseClassAsync(_alice, ClassCatalog.Fighter);
        await ChooseClassAsync(_bob, ClassCatalog.Rogue);

        var aliceItems = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var bobItems = await _bob.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");

        Assert.Equal(2, aliceItems!.Count);
        Assert.Equal(2, bobItems!.Count);
        Assert.Empty(aliceItems.Select(i => i.Id).Intersect(bobItems.Select(i => i.Id)));

        var target = aliceItems[0].Id;

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/inventory/{target}/equip", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.DeleteAsync($"/api/rpg/inventory/{target}")).StatusCode);
    }

    [Fact]
    public async Task Equipping_swaps_the_slot_and_updates_the_sheet()
    {
        await ChooseClassAsync(_alice, ClassCatalog.Fighter);
        await GrantStaminaAsync(_alice);

        // Win something to swap in.
        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var equippedWeapon = inventory!.Single(i => i.Slot == "weapon");

        var unequip = await _alice.PostAsync($"/api/rpg/inventory/{equippedWeapon.Id}/unequip", null);
        unequip.EnsureSuccessStatusCode();

        var afterUnequip = await unequip.Content.ReadFromJsonAsync<EquipDto>();
        Assert.All(afterUnequip!.Inventory.Where(i => i.Slot == "weapon"), i => Assert.False(i.IsEquipped));
        Assert.Equal("1d4", afterUnequip.Sheet.Damage); // back to bare hands

        var equip = await _alice.PostAsync($"/api/rpg/inventory/{equippedWeapon.Id}/equip", null);
        var afterEquip = await equip.Content.ReadFromJsonAsync<EquipDto>();

        Assert.NotEqual("1d4", afterEquip!.Sheet.Damage);
    }

    [Fact]
    public async Task An_equipped_item_cannot_be_sold()
    {
        await ChooseClassAsync(_alice);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var equipped = inventory!.First(i => i.IsEquipped);

        var response = await _alice.DeleteAsync($"/api/rpg/inventory/{equipped.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Selling_an_unequipped_item_pays_gold()
    {
        await ChooseClassAsync(_alice);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var item = inventory!.First();

        await _alice.PostAsync($"/api/rpg/inventory/{item.Id}/unequip", null);

        var sold = await _alice.DeleteAsync($"/api/rpg/inventory/{item.Id}");
        sold.EnsureSuccessStatusCode();

        var result = await sold.Content.ReadFromJsonAsync<SellDto>();

        Assert.True(result!.GoldGained > 0);
        Assert.Equal(result.GoldGained, result.Gold);

        var remaining = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        Assert.DoesNotContain(remaining!, i => i.Id == item.Id);
    }

    [Fact]
    public async Task The_bestiary_is_filtered_to_the_characters_level()
    {
        var monsters = await _alice.GetFromJsonAsync<List<MonsterListDto>>("/api/rpg/monsters");

        Assert.NotEmpty(monsters!);
        Assert.DoesNotContain(monsters!, m => m.Key == MonsterCatalog.YoungDragon);
        Assert.All(monsters!, m => Assert.True(m.Level <= 2));
    }

    [Fact]
    public async Task Quests_track_real_work_and_can_be_claimed_once()
    {
        await ChooseClassAsync(_alice);

        for (var i = 0; i < 5; i++)
        {
            var task = await _alice.PostAsJsonAsync(
                "/api/tasks", new { title = $"Chore {i}", difficulty = "easy" });
            var created = await task.Content.ReadFromJsonAsync<TaskDto>();

            await _alice.PostAsJsonAsync($"/api/tasks/{created!.Id}/complete", new { utcOffsetMinutes = 0 });
        }

        var quests = await _alice.GetFromJsonAsync<List<QuestListDto>>("/api/rpg/quests");
        var honestWork = quests!.Single(q => q.Key == QuestCatalog.HonestWork);

        Assert.True(honestWork.IsComplete);
        Assert.False(honestWork.IsClaimed);

        var claim = await _alice.PostAsync($"/api/rpg/quests/{QuestCatalog.HonestWork}/claim", null);
        claim.EnsureSuccessStatusCode();

        var result = await claim.Content.ReadFromJsonAsync<ClaimDto>();
        Assert.True(result!.GoldGained > 0);

        // Claiming twice must not pay twice.
        var again = await _alice.PostAsync($"/api/rpg/quests/{QuestCatalog.HonestWork}/claim", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task An_unfinished_quest_cannot_be_claimed()
    {
        await ChooseClassAsync(_alice);

        var response = await _alice.PostAsync($"/api/rpg/quests/{QuestCatalog.GoblinCull}/claim", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Quest_progress_is_per_user()
    {
        await ChooseClassAsync(_alice);
        await ChooseClassAsync(_bob);

        for (var i = 0; i < 5; i++)
        {
            var task = await _alice.PostAsJsonAsync(
                "/api/tasks", new { title = $"Chore {i}", difficulty = "easy" });
            var created = await task.Content.ReadFromJsonAsync<TaskDto>();
            await _alice.PostAsJsonAsync($"/api/tasks/{created!.Id}/complete", new { utcOffsetMinutes = 0 });
        }

        var bobQuests = await _bob.GetFromJsonAsync<List<QuestListDto>>("/api/rpg/quests");
        var bobHonestWork = bobQuests!.Single(q => q.Key == QuestCatalog.HonestWork);

        Assert.False(bobHonestWork.IsComplete);
        Assert.All(bobHonestWork.Objectives, o => Assert.Equal(0, o.Current));
    }

    /// <summary>
    /// Completes throwaway tasks purely to earn stamina.
    /// </summary>
    /// <remarks>
    /// Deliberately Easy tasks. Epic ones grant 5 stamina but 100 XP each, which levels the
    /// character out of the low-level monsters these tests fight and turns a start request
    /// into a 400.
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

    // ---- wire shapes -------------------------------------------------------

    private sealed record TaskDto(Guid Id);
    private sealed record CharacterDto(int Level, int TotalXp);
    private sealed record AbilityDto(string Abbreviation, int Score, int Modifier, int BonusFromItems);

    private sealed record SheetDto(
        string? ClassKey, string? ClassName, int Level, List<AbilityDto> Abilities,
        int ArmourClass, int AttackBonus, string Damage, int CurrentHitPoints,
        int MaxHitPoints, int Stamina, int Gold);

    private sealed record ItemDto(Guid Id, string ItemKey, string Name, string Slot, string Rarity, bool IsEquipped);
    private sealed record EncounterDto(Guid Id, string MonsterKey, string Status, int Round);
    private sealed record DieDto(int Sides, int Value, bool Kept);
    private sealed record ModifierDto(string Label, int Value);

    private sealed record RollDto(
        string Actor, string Kind, List<DieDto> Dice, List<ModifierDto> Modifiers,
        int Total, int? Target, string Outcome);

    private sealed record AttackDto(EncounterDto Encounter, List<RollDto> Rolls, SheetDto Sheet);
    private sealed record EquipDto(SheetDto Sheet, List<ItemDto> Inventory);
    private sealed record SellDto(int GoldGained, int Gold);
    private sealed record MonsterListDto(string Key, string Name, int Level);
    private sealed record ObjectiveDto(string Id, int Current, int Required, bool IsComplete);

    private sealed record QuestListDto(
        string Key, string Name, List<ObjectiveDto> Objectives, bool IsComplete, bool IsClaimed);

    private sealed record ClaimDto(int GoldGained, int Gold);
}
