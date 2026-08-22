using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// The routes added by the expansion: chronicle, rest, shop, upgrades and abilities.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class ExpansionEndpointTests(PostgresFixture postgres) : IAsyncLifetime
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

    private static async Task ChooseAsync(HttpClient client, string classKey = ClassCatalog.Fighter) =>
        (await client.PutAsJsonAsync("/api/rpg/class", new { classKey })).EnsureSuccessStatusCode();

    /// <summary>Completes Easy tasks so stamina accrues without levelling out of range.</summary>
    private static async Task StaminaAsync(HttpClient client, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var created = await (await client.PostAsJsonAsync(
                "/api/tasks", new { title = $"Chore {i}", difficulty = "easy" }))
                .Content.ReadFromJsonAsync<IdDto>();

            await client.PostAsJsonAsync($"/api/tasks/{created!.Id}/complete", new { utcOffsetMinutes = 0 });
        }
    }

    private async Task GrantGoldAsync(Guid _, string subject, int gold)
    {
        await using var db = postgres.CreateContext();
        var user = await db.Users.SingleAsync(u => u.Auth0Sub == subject);
        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Gold = gold;
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("/api/rpg/encounters")]
    [InlineData("/api/rpg/shop")]
    public async Task New_read_routes_require_authentication(string route)
    {
        using var anonymous = _factory.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
    }

    // ---------------------------------------------------------------- chronicle

    [Fact]
    public async Task The_chronicle_records_finished_fights_and_ignores_active_ones()
    {
        await ChooseAsync(_alice);
        await StaminaAsync(_alice, 4);

        var empty = await _alice.GetFromJsonAsync<ChronicleDto>("/api/rpg/encounters");
        Assert.Empty(empty!.Encounters);
        Assert.Equal(0, empty.Summary.Fought);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });
        start.EnsureSuccessStatusCode();
        var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();

        // Still active: nothing in the chronicle yet.
        var during = await _alice.GetFromJsonAsync<ChronicleDto>("/api/rpg/encounters");
        Assert.Empty(during!.Encounters);

        await _alice.PostAsync($"/api/rpg/encounters/{encounter!.Id}/flee", null);

        var after = await _alice.GetFromJsonAsync<ChronicleDto>("/api/rpg/encounters");
        Assert.Single(after!.Encounters);
        Assert.Equal(1, after.Summary.Fought);
        Assert.Equal(1, after.Summary.Fled);

        // The full roll-by-roll log comes back with it.
        Assert.NotEmpty(after.Encounters[0].Log);
    }

    [Fact]
    public async Task One_adventurers_chronicle_is_invisible_to_another()
    {
        await ChooseAsync(_alice);
        await ChooseAsync(_bob);
        await StaminaAsync(_alice, 2);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });
        var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();
        await _alice.PostAsync($"/api/rpg/encounters/{encounter!.Id}/flee", null);

        var bobs = await _bob.GetFromJsonAsync<ChronicleDto>("/api/rpg/encounters");

        Assert.Empty(bobs!.Encounters);
        Assert.Equal(0, bobs.Summary.Fought);
    }

    // --------------------------------------------------------------------- rest

    [Fact]
    public async Task The_sheet_advertises_regeneration_and_the_price_of_a_bed()
    {
        await ChooseAsync(_alice);

        await using (var db = postgres.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Auth0Sub == "auth0|alice");
            var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
            character.CurrentHitPoints = 2;
            character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        Assert.NotNull(sheet!.NextRegenerationAt);
        Assert.NotNull(sheet.FullyHealedAt);
        Assert.True(sheet.FullyHealedAt >= sheet.NextRegenerationAt);
        Assert.True(sheet.RestCost > 0);
    }

    [Fact]
    public async Task Resting_costs_gold_and_heals_to_full()
    {
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 5000);

        await using (var db = postgres.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Auth0Sub == "auth0|alice");
            var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
            character.CurrentHitPoints = 1;
            character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var before = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        var rest = await _alice.PostAsync("/api/rpg/rest", null);
        rest.EnsureSuccessStatusCode();

        var result = await rest.Content.ReadFromJsonAsync<RestDto>();

        Assert.Equal(before!.RestCost, result!.GoldSpent);
        Assert.Equal(result.MaxHitPoints, result.HitPoints);
        Assert.Equal(5000 - result.GoldSpent, result.Gold);
    }

    [Fact]
    public async Task Resting_at_full_health_is_refused()
    {
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 1000);

        var response = await _alice.PostAsync("/api/rpg/rest", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Resting_without_the_gold_is_refused()
    {
        await ChooseAsync(_alice);

        await using (var db = postgres.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Auth0Sub == "auth0|alice");
            var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
            character.CurrentHitPoints = 1;
            character.Gold = 0;
            character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var response = await _alice.PostAsync("/api/rpg/rest", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task You_cannot_bed_down_mid_fight()
    {
        await ChooseAsync(_alice);
        await StaminaAsync(_alice, 2);
        await GrantGoldAsync(default, "auth0|alice", 5000);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });
        start.EnsureSuccessStatusCode();

        var response = await _alice.PostAsync("/api/rpg/rest", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --------------------------------------------------------------------- shop

    [Fact]
    public async Task The_shop_stocks_six_affordable_flagged_offers()
    {
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 40);

        var shop = await _alice.GetFromJsonAsync<ShopDto>("/api/rpg/shop");

        Assert.Equal(6, shop!.Offers.Count);
        Assert.Equal(40, shop.Gold);
        Assert.True(shop.RotatesAt > DateTimeOffset.UtcNow);
        Assert.All(shop.Offers, o => Assert.Equal(o.Price <= 40, o.Affordable));
    }

    [Fact]
    public async Task Buying_transfers_gold_for_an_item()
    {
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 100_000);

        var shop = await _alice.GetFromJsonAsync<ShopDto>("/api/rpg/shop");
        var offer = shop!.Offers[0];

        var buy = await _alice.PostAsync($"/api/rpg/shop/{offer.OfferId}/buy", null);
        buy.EnsureSuccessStatusCode();

        var result = await buy.Content.ReadFromJsonAsync<PurchaseDto>();

        Assert.Equal(offer.Price, result!.GoldSpent);
        Assert.Equal(100_000 - offer.Price, result.Gold);
        Assert.Equal(offer.ItemKey, result.Item.ItemKey);
        Assert.Equal(offer.Rarity, result.Item.Rarity);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        Assert.Contains(inventory!, i => i.Id == result.Item.Id);
    }

    [Fact]
    public async Task An_offer_can_only_be_bought_once_a_day()
    {
        // The shelf is a pure function of the user and the date, so without a record of the
        // purchase the same offer id is buyable for as long as the gold lasts, and the forge
        // turns every copy into essence. Six offers a day is the whole cap.
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 100_000);

        var shop = await _alice.GetFromJsonAsync<ShopDto>("/api/rpg/shop");
        var offer = shop!.Offers[0];

        Assert.All(shop.Offers, o => Assert.False(o.SoldOut));

        var first = await _alice.PostAsync($"/api/rpg/shop/{offer.OfferId}/buy", null);
        first.EnsureSuccessStatusCode();

        var second = await _alice.PostAsync($"/api/rpg/shop/{offer.OfferId}/buy", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // Paid once, and holding one of them.
        var after = await _alice.GetFromJsonAsync<ShopDto>("/api/rpg/shop");

        Assert.Equal(100_000 - offer.Price, after!.Gold);
        Assert.True(after.Offers.Single(o => o.OfferId == offer.OfferId).SoldOut);
        Assert.All(after.Offers.Where(o => o.OfferId != offer.OfferId), o => Assert.False(o.SoldOut));

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");

        Assert.Single(inventory!, i => i.ItemKey == offer.ItemKey && !i.IsEquipped);
    }

    [Fact]
    public async Task A_sold_out_offer_blocks_only_the_offer_and_only_for_its_owner()
    {
        // Sold out is per user per offer. Bob's shelf is rolled from his own id, and Alice
        // buying the whole of hers must not empty his.
        await ChooseAsync(_alice);
        await ChooseAsync(_bob);
        await GrantGoldAsync(default, "auth0|alice", 100_000);
        await GrantGoldAsync(default, "auth0|bob", 100_000);

        var alices = await _alice.GetFromJsonAsync<ShopDto>("/api/rpg/shop");

        foreach (var offer in alices!.Offers)
        {
            (await _alice.PostAsync($"/api/rpg/shop/{offer.OfferId}/buy", null)).EnsureSuccessStatusCode();
        }

        var emptied = await _alice.GetFromJsonAsync<ShopDto>("/api/rpg/shop");
        Assert.All(emptied!.Offers, o => Assert.True(o.SoldOut));

        var bobs = await _bob.GetFromJsonAsync<ShopDto>("/api/rpg/shop");
        Assert.All(bobs!.Offers, o => Assert.False(o.SoldOut));

        (await _bob.PostAsync($"/api/rpg/shop/{bobs.Offers[0].OfferId}/buy", null))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_forged_offer_cannot_be_bought()
    {
        // Stock is recomputed server-side precisely so an offer id cannot be invented.
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 100_000);

        var response = await _alice.PostAsync(
            "/api/rpg/shop/20260101-0-dragonfang-spear/buy", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Another_users_offer_id_buys_nothing()
    {
        await ChooseAsync(_alice);
        await ChooseAsync(_bob);
        await GrantGoldAsync(default, "auth0|bob", 100_000);

        var alicesShop = await _alice.GetFromJsonAsync<ShopDto>("/api/rpg/shop");
        var bobsShop = await _bob.GetFromJsonAsync<ShopDto>("/api/rpg/shop");

        // Different shelves, so Alice's ids mean nothing to Bob's shop.
        var aliceOnly = alicesShop!.Offers
            .Select(o => o.OfferId)
            .Except(bobsShop!.Offers.Select(o => o.OfferId))
            .FirstOrDefault();

        Assert.NotNull(aliceOnly);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/shop/{aliceOnly}/buy", null)).StatusCode);
    }

    [Fact]
    public async Task Buying_without_the_gold_is_refused()
    {
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 0);

        var shop = await _alice.GetFromJsonAsync<ShopDto>("/api/rpg/shop");

        var response = await _alice.PostAsync($"/api/rpg/shop/{shop!.Offers[0].OfferId}/buy", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ------------------------------------------------------------------ upgrade

    [Fact]
    public async Task Upgrading_raises_rarity_for_gold()
    {
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 100_000);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var item = inventory!.First();

        Assert.Equal("common", item.Rarity);

        var upgrade = await _alice.PostAsync($"/api/rpg/inventory/{item.Id}/upgrade", null);
        upgrade.EnsureSuccessStatusCode();

        var result = await upgrade.Content.ReadFromJsonAsync<UpgradeDto>();

        Assert.Equal("common", result!.From);
        Assert.Equal("uncommon", result.To);
        Assert.True(result.GoldSpent > 0);
        Assert.Equal("uncommon", result.Item.Rarity);
    }

    [Fact]
    public async Task Legendary_is_as_far_as_it_goes()
    {
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 1_000_000);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        var id = inventory!.First().Id;

        for (var step = 0; step < 4; step++)
        {
            (await _alice.PostAsync($"/api/rpg/inventory/{id}/upgrade", null)).EnsureSuccessStatusCode();
        }

        var response = await _alice.PostAsync($"/api/rpg/inventory/{id}/upgrade", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Upgrading_buys_tier_and_never_a_new_word()
    {
        // Gold buys tiers; dice and essence buy words. A Rare item carrying one prefix comes
        // back from an upgrade as an Epic carrying the same one prefix, worth more, with a
        // second slot it has to pay the forge to fill.
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 100_000);

        Guid id;

        await using (var db = postgres.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Auth0Sub == "auth0|alice");

            var item = new InventoryItem
            {
                UserId = user.Id,
                ItemKey = ItemCatalog.SilveredBlade,
                Slot = ItemSlot.Weapon,
                Rarity = Rarity.Rare,
                PrefixKey = AffixCatalog.Vicious
            };

            db.InventoryItems.Add(item);
            await db.SaveChangesAsync();

            id = item.Id;
        }

        var upgrade = await _alice.PostAsync($"/api/rpg/inventory/{id}/upgrade", null);
        upgrade.EnsureSuccessStatusCode();

        var result = await upgrade.Content.ReadFromJsonAsync<UpgradeDto>();

        Assert.Equal("epic", result!.Item.Rarity);
        Assert.Equal("Vicious", result.Item.Prefix);
        Assert.Null(result.Item.Suffix);
        Assert.Equal(2, result.Item.AffixSlots);
        Assert.Equal(ForgeRules.EssenceFor(Rarity.Epic, 1), result.Item.SalvageValue);
    }

    /// <summary>
    /// The bench quotes the outcome before the gold is spent, so the quote has to be the outcome.
    /// </summary>
    /// <remarks>
    /// The preview is a second computation of what the upgrade does. Two of those drift, and a
    /// screen that promises plus one armour and delivers nothing is worse than the screen that
    /// promised nothing at all, which is what this replaced. Asserted field by field against the
    /// item that comes back rather than against hand-written numbers, so the rules stay the only
    /// source and this fails the moment the two disagree.
    /// </remarks>
    [Fact]
    public async Task The_preview_is_what_the_upgrade_actually_does()
    {
        await ChooseAsync(_alice);
        await GrantGoldAsync(default, "auth0|alice", 100_000);

        Guid id;

        await using (var db = postgres.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Auth0Sub == "auth0|alice");

            // Rare armour carrying a word: the one step that moves armour, doubles a word and
            // opens a slot all at once, so every field of the preview is under test.
            var item = new InventoryItem
            {
                UserId = user.Id,
                ItemKey = ItemCatalog.ChainShirt,
                Slot = ItemSlot.Armour,
                Rarity = Rarity.Rare,
                PrefixKey = AffixCatalog.Warded
            };

            db.InventoryItems.Add(item);
            await db.SaveChangesAsync();

            id = item.Id;
        }

        var before = (await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory"))!
            .Single(i => i.Id == id);

        var quoted = before.Upgrade;
        Assert.NotNull(quoted);

        var upgrade = await _alice.PostAsync($"/api/rpg/inventory/{id}/upgrade", null);
        upgrade.EnsureSuccessStatusCode();

        var result = await upgrade.Content.ReadFromJsonAsync<UpgradeDto>();
        var after = result!.Item;

        Assert.Equal(quoted.ToRarity, after.Rarity);
        Assert.Equal(quoted.Cost, result.GoldSpent);
        Assert.Equal(quoted.ArmourBonus, after.ArmourBonus);
        Assert.Equal(quoted.AffixSlots, after.AffixSlots);
        Assert.Equal(
            quoted.AbilityBonuses.Select(b => (b.Label, b.Value)),
            after.AbilityBonuses.Select(b => (b.Label, b.Value)));

        // Rare to Epic is the boundary the tier table steps at, so the promise was "twice as
        // strong" and the armour has to have moved by more than the plain rarity point.
        Assert.True(quoted.AffixesGrow);
        Assert.True(after.ArmourBonus - before.ArmourBonus > 1);
    }

    [Fact]
    public async Task Nothing_the_bench_refuses_carries_a_preview()
    {
        // Null is the whole eligibility test on the client, so it has to agree with the three
        // refusals in UpgradeAsync. The bench asked "is it Legendary" and so offered potions.
        await ChooseAsync(_alice);

        await using (var db = postgres.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Auth0Sub == "auth0|alice");

            db.InventoryItems.Add(new InventoryItem
            {
                UserId = user.Id,
                ItemKey = ItemCatalog.SilveredBlade,
                Slot = ItemSlot.Weapon,
                Rarity = Rarity.Legendary
            });

            db.InventoryItems.Add(new InventoryItem
            {
                UserId = user.Id,
                ItemKey = ItemCatalog.DraughtOfMending,
                Slot = ItemSlot.Consumable,
                Rarity = Rarity.Common,
                Quantity = 2
            });

            await db.SaveChangesAsync();
        }

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");

        Assert.Null(inventory!.Single(i => i.Rarity == "legendary").Upgrade);
        Assert.All(
            inventory.Where(i => i.ItemKey == ItemCatalog.DraughtOfMending),
            i => Assert.Null(i.Upgrade));

        // And everything the bench does accept quotes a price.
        Assert.All(
            inventory.Where(i => i.Upgrade is not null),
            i => Assert.True(i.Upgrade!.Cost >= 25));
    }

    [Fact]
    public async Task Another_users_item_cannot_be_upgraded()
    {
        await ChooseAsync(_alice);
        await ChooseAsync(_bob);
        await GrantGoldAsync(default, "auth0|bob", 100_000);

        var alices = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _bob.PostAsync($"/api/rpg/inventory/{alices![0].Id}/upgrade", null)).StatusCode);
    }

    // ---------------------------------------------------------------- abilities

    [Fact]
    public async Task The_sheet_lists_the_classes_abilities()
    {
        await ChooseAsync(_alice, ClassCatalog.Wizard);

        var sheet = await _alice.GetFromJsonAsync<SheetDto>("/api/rpg/sheet");

        Assert.NotEmpty(sheet!.ClassAbilities);
        Assert.Contains(sheet.ClassAbilities, a => a.Key == ClassAbilities.MagicMissile);

        // Outside a fight, uses read as full rather than zero.
        Assert.All(sheet.ClassAbilities, a => Assert.Equal(a.UsesPerEncounter, a.Remaining));
    }

    [Fact]
    public async Task Using_an_ability_reports_the_uses_left()
    {
        await ChooseAsync(_alice, ClassCatalog.Wizard);
        await StaminaAsync(_alice, 3);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });
        start.EnsureSuccessStatusCode();
        var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();

        var use = await _alice.PostAsync(
            $"/api/rpg/encounters/{encounter!.Id}/ability/{ClassAbilities.MagicMissile}", null);
        use.EnsureSuccessStatusCode();

        var result = await use.Content.ReadFromJsonAsync<AttackDto>();
        var missile = result!.Sheet.ClassAbilities.Single(a => a.Key == ClassAbilities.MagicMissile);

        Assert.Equal(missile.UsesPerEncounter - 1, missile.Remaining);
    }

    [Fact]
    public async Task An_ability_that_is_not_yours_is_rejected()
    {
        await ChooseAsync(_alice, ClassCatalog.Fighter);
        await StaminaAsync(_alice, 3);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });
        var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();

        var response = await _alice.PostAsync(
            $"/api/rpg/encounters/{encounter!.Id}/ability/{ClassAbilities.MagicMissile}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------ quests

    [Fact]
    public async Task Quests_above_the_characters_level_are_shown_locked_rather_than_hidden()
    {
        await ChooseAsync(_alice);

        var quests = await _alice.GetFromJsonAsync<List<QuestDto>>("/api/rpg/quests");

        // Every quest in the catalog is visible, so there is always something to aim for.
        Assert.Equal(QuestCatalog.All.Count, quests!.Count);
        Assert.Contains(quests, q => q.IsLocked);
        Assert.All(quests.Where(q => q.IsLocked), q => Assert.True(q.MinimumLevel > 1));
    }

    // -------------------------------------------------------- the invariant

    [Fact]
    public async Task Nothing_in_the_expansion_can_move_experience()
    {
        await ChooseAsync(_alice, ClassCatalog.Wizard);
        await StaminaAsync(_alice, 6);
        await GrantGoldAsync(default, "auth0|alice", 100_000);

        var before = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        // Shop, upgrade, rest, ability, chronicle: every new way to spend a turn or a coin.
        var shop = await _alice.GetFromJsonAsync<ShopDto>("/api/rpg/shop");
        await _alice.PostAsync($"/api/rpg/shop/{shop!.Offers[0].OfferId}/buy", null);

        var inventory = await _alice.GetFromJsonAsync<List<ItemDto>>("/api/rpg/inventory");
        await _alice.PostAsync($"/api/rpg/inventory/{inventory![0].Id}/upgrade", null);

        var start = await _alice.PostAsJsonAsync(
            "/api/rpg/encounters", new { monsterKey = MonsterCatalog.GiantRat });
        var encounter = await start.Content.ReadFromJsonAsync<EncounterDto>();

        await _alice.PostAsync(
            $"/api/rpg/encounters/{encounter!.Id}/ability/{ClassAbilities.MagicMissile}", null);
        await _alice.PostAsync($"/api/rpg/encounters/{encounter.Id}/flee", null);
        await _alice.PostAsync("/api/rpg/rest", null);
        await _alice.GetAsync("/api/rpg/encounters");

        var after = await _alice.GetFromJsonAsync<CharacterDto>("/api/character");

        Assert.Equal(before!.TotalXp, after!.TotalXp);
        Assert.Equal(before.Level, after.Level);
    }

    // ---- wire shapes -------------------------------------------------------

    private sealed record IdDto(Guid Id);
    private sealed record CharacterDto(int Level, int TotalXp);
    private sealed record AbilityDto(string Key, string Name, int UsesPerEncounter, int Remaining);

    private sealed record TierDto(int Pieces, string Description, bool Active);

    private sealed record SetDto(
        string Key, string Name, string Blurb, int Equipped, int Total, List<TierDto> Tiers);

    private sealed record SheetDto(
        string? ClassKey, int Level, List<AbilityDto> ClassAbilities,
        int CurrentHitPoints, int MaxHitPoints, int Gold, int RestCost,
        DateTimeOffset? NextRegenerationAt, DateTimeOffset? FullyHealedAt,
        int Essence, List<SetDto> Sets);

    private sealed record ItemDto(
        Guid Id, string ItemKey, string Name, string Rarity, bool IsEquipped,
        string? Prefix, string? Suffix, string? SetName, int AffixSlots,
        int SalvageValue, int ImbueCost, int ReforgeCost,
        int ArmourBonus, List<ModifierDto> AbilityBonuses, UpgradePreviewDto? Upgrade);
    private sealed record ModifierDto(string Label, int Value);
    private sealed record UpgradePreviewDto(
        string ToRarity, int Cost, int ArmourBonus,
        List<ModifierDto> AbilityBonuses, int AffixSlots, bool AffixesGrow);
    private sealed record LogDto(string Kind, string Text);
    private sealed record EncounterDto(Guid Id, string Status, int Round, List<LogDto> Log, List<StatusEffectDto> Effects);

    private sealed record StatusEffectDto(string Kind, string Target, int Rounds, int Magnitude, string Source);
    private sealed record AttackDto(EncounterDto Encounter, SheetDto Sheet);
    private sealed record SummaryDto(int Fought, int Won, int Lost, int Fled, int GoldEarned);
    private sealed record ChronicleDto(SummaryDto Summary, List<EncounterDto> Encounters);
    private sealed record OfferDto(
        string OfferId, string ItemKey, string Rarity, int Price, bool Affordable, bool SoldOut);
    private sealed record ShopDto(List<OfferDto> Offers, DateTimeOffset RotatesAt, int Gold);
    private sealed record PurchaseDto(ItemDto Item, int GoldSpent, int Gold);
    private sealed record UpgradeDto(ItemDto Item, string From, string To, int GoldSpent, int Gold);
    private sealed record RestDto(int GoldSpent, int Gold, int HitPoints, int MaxHitPoints);
    private sealed record QuestDto(string Key, bool IsLocked, int MinimumLevel);
}
