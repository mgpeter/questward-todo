using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services.Rpg;
using TodoApp.Data;
using TodoApp.Models.Dice;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// The forge and the drop path, driven by scripted dice so an affix is an assertion rather
/// than a hope. The endpoint suite runs against the real roller and stays dice-agnostic; this
/// is where the actual words are pinned down.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class ForgeServiceTests(PostgresFixture postgres)
{
    private sealed record Harness(
        TodoDbContext Db,
        ForgeService Forge,
        AdventurerService Adventurer,
        CharacterSheetService Sheets,
        Guid UserId);

    /// <param name="roller">
    /// Handed to the forge only. Class selection grants gear rather than rolling for it, so a
    /// script that starts empty is still untouched when the first test line runs.
    /// </param>
    private async Task<Harness> ArrangeAsync(IDiceRoller roller, int essence = 0)
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|smith");

        var db = postgres.CreateContext();
        var sheets = new CharacterSheetService(db);
        var loot = new LootService(db, new FixedDiceRoller(1));
        var adventurer = new AdventurerService(db, sheets, loot);
        var forge = new ForgeService(db, roller);

        await adventurer.ChooseClassAsync(user.Id, ClassCatalog.Fighter, TestContext.Current.CancellationToken);

        var character = await db.Characters.SingleAsync(c => c.UserId == user.Id);
        character.Essence = essence;
        await db.SaveChangesAsync();

        return new Harness(db, forge, adventurer, sheets, user.Id);
    }

    private static async Task<InventoryItem> GiveAsync(
        Harness harness,
        string itemKey,
        Rarity rarity,
        string? prefix = null,
        string? suffix = null,
        bool equipped = false)
    {
        var item = new InventoryItem
        {
            UserId = harness.UserId,
            ItemKey = itemKey,
            Slot = ItemCatalog.Find(itemKey)?.Slot ?? ItemSlot.Weapon,
            Rarity = rarity,
            PrefixKey = prefix,
            SuffixKey = suffix,
            IsEquipped = equipped
        };

        harness.Db.InventoryItems.Add(item);
        await harness.Db.SaveChangesAsync();

        return item;
    }

    private Task<TodoApp.Models.Character> CharacterAsync(Harness harness) =>
        harness.Db.Characters.AsNoTracking().SingleAsync(c => c.UserId == harness.UserId);

    // ------------------------------------------------------------------ drops

    [Fact]
    public async Task A_common_drop_carries_no_words_and_spends_no_extra_dice()
    {
        // Three rolls and no more: does it drop, which item, which rarity. Half of all drops
        // are Common, so an extra die here would shift every seeded script in the suite.
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var roller = new SequenceDiceRoller(1, 1, 20);
        var loot = new LootService(harness.Db, roller);

        var drop = loot.RollDrop(harness.UserId, MonsterCatalog.Find(MonsterCatalog.Goblin)!, false);

        Assert.NotNull(drop);
        Assert.Equal(Rarity.Common, drop.Rarity);
        Assert.Null(drop.PrefixKey);
        Assert.Null(drop.SuffixKey);
        Assert.Equal("Goblin Cleaver", drop.DisplayName);
        Assert.Equal(3, roller.RollCount);
    }

    [Theory]
    [InlineData(60, Rarity.Uncommon, 4)]
    [InlineData(90, Rarity.Rare, 4)]
    [InlineData(95, Rarity.Epic, 5)]
    [InlineData(100, Rarity.Legendary, 5)]
    public async Task A_drop_above_common_rolls_exactly_the_words_its_rarity_allows(
        int rarityFace,
        Rarity rarity,
        int expectedRolls)
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var roller = new SequenceDiceRoller(1, 1, rarityFace, 1, 1);
        var loot = new LootService(harness.Db, roller);

        var drop = loot.RollDrop(harness.UserId, MonsterCatalog.Find(MonsterCatalog.Goblin)!, false)!;

        Assert.Equal(rarity, drop.Rarity);
        Assert.Equal(AffixRules.RollableFor(drop.Slot, rarity), AffixRules.CountInForce(drop));
        Assert.Equal(expectedRolls, roller.RollCount);
    }

    [Fact]
    public async Task The_same_script_always_drops_the_same_item_with_the_same_words()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var goblin = MonsterCatalog.Find(MonsterCatalog.Goblin)!;

        var first = new LootService(harness.Db, new SequenceDiceRoller(1, 1, 95, 4, 2))
            .RollDrop(harness.UserId, goblin, false)!;

        var second = new LootService(harness.Db, new SequenceDiceRoller(1, 1, 95, 4, 2))
            .RollDrop(harness.UserId, goblin, false)!;

        Assert.Equal(first.ItemKey, second.ItemKey);
        Assert.Equal(first.PrefixKey, second.PrefixKey);
        Assert.Equal(first.SuffixKey, second.SuffixKey);

        // And the words are the ones the pool order says they are, prefix rolled before suffix.
        Assert.Equal(AffixCatalog.Keen, first.PrefixKey);
        Assert.Equal(AffixCatalog.OfTheFox, first.SuffixKey);
        Assert.Equal("Keen Goblin Cleaver of the Fox", first.DisplayName);
    }

    [Fact]
    public async Task Granted_gear_is_never_rolled_for_words()
    {
        // Starting gear and quest rewards are promises the catalog already made. A die here
        // would pay two people differently for the same piece of real work.
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var roller = new SequenceDiceRoller();
        var loot = new LootService(harness.Db, roller);

        var granted = await loot.GrantAsync(
            harness.UserId, ItemCatalog.SilveredBlade, Rarity.Legendary,
            TestContext.Current.CancellationToken);

        Assert.Null(granted.PrefixKey);
        Assert.Null(granted.SuffixKey);
        Assert.Equal(0, roller.RollCount);
    }

    [Fact]
    public async Task Starting_gear_arrives_plain()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));

        var items = await harness.Db.InventoryItems
            .Where(i => i.UserId == harness.UserId)
            .ToListAsync();

        Assert.Equal(2, items.Count);
        Assert.All(items, i =>
        {
            Assert.Null(i.PrefixKey);
            Assert.Null(i.SuffixKey);
        });
    }

    // ---------------------------------------------------------------- salvage

    [Fact]
    public async Task Salvaging_destroys_the_item_and_pays_essence()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Rare, AffixCatalog.Keen);

        var result = await harness.Forge.SalvageAsync(harness.UserId, item.Id, default);

        Assert.True(result.Ok);
        Assert.Equal(7, result.Value!.EssenceGained);   // 5 for Rare, 2 for the word
        Assert.Equal(7, result.Value.Essence);
        Assert.Equal(7, (await CharacterAsync(harness)).Essence);
        Assert.Null(await harness.Db.InventoryItems.FirstOrDefaultAsync(i => i.Id == item.Id));
    }

    [Fact]
    public async Task Salvaging_pays_no_gold()
    {
        // One item, one choice. Selling pays gold and breaking pays essence, and that choice
        // is the whole reason the material exists.
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Epic);

        var goldBefore = (await CharacterAsync(harness)).Gold;

        Assert.True((await harness.Forge.SalvageAsync(harness.UserId, item.Id, default)).Ok);

        Assert.Equal(goldBefore, (await CharacterAsync(harness)).Gold);
    }

    [Fact]
    public async Task Salvage_yield_rises_with_rarity_and_with_affixes()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));

        var plain = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Uncommon);
        var rolled = await GiveAsync(
            harness, ItemCatalog.SilveredBlade, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheFox);

        var forPlain = await harness.Forge.SalvageAsync(harness.UserId, plain.Id, default);
        var forRolled = await harness.Forge.SalvageAsync(harness.UserId, rolled.Id, default);

        Assert.Equal(2, forPlain.Value!.EssenceGained);
        Assert.Equal(16, forRolled.Value!.EssenceGained);
        Assert.Equal(18, forRolled.Value.Essence);
    }

    [Fact]
    public async Task An_equipped_item_cannot_be_salvaged()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));

        var equipped = await harness.Db.InventoryItems
            .FirstAsync(i => i.UserId == harness.UserId && i.IsEquipped);

        var result = await harness.Forge.SalvageAsync(harness.UserId, equipped.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.ItemEquipped, result.Failure);
        Assert.NotNull(await harness.Db.InventoryItems.FirstOrDefaultAsync(i => i.Id == equipped.Id));
    }

    [Fact]
    public async Task A_retired_item_key_still_salvages()
    {
        // A key that has left the catalog must not strand a row in someone's bag forever,
        // which is the ruling the sell path already makes.
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var item = await GiveAsync(harness, "axe-of-a-forgotten-patch", Rarity.Legendary);

        var result = await harness.Forge.SalvageAsync(harness.UserId, item.Id, default);

        Assert.True(result.Ok);
        Assert.Equal(1, result.Value!.EssenceGained);
        Assert.Null(await harness.Db.InventoryItems.FirstOrDefaultAsync(i => i.Id == item.Id));
    }

    [Fact]
    public async Task Another_users_item_cannot_be_salvaged()
    {
        // Scoped in the query itself, so another user's id is indistinguishable from one that
        // never existed rather than a 403 that confirms it.
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var thief = await postgres.CreateUserAsync("test|thief");

        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Epic);

        var result = await harness.Forge.SalvageAsync(thief.Id, item.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.NotFound, result.Failure);
        Assert.NotNull(await harness.Db.InventoryItems.FirstOrDefaultAsync(i => i.Id == item.Id));
        Assert.Equal(0, (await CharacterAsync(harness)).Essence);
    }

    // ------------------------------------------------------------------ imbue

    [Fact]
    public async Task Imbuing_fills_an_empty_slot()
    {
        // A Rare weapon holds one word and has neither, so this is one die over the combined
        // pool of thirteen, exactly as a one-slot drop rolls.
        var harness = await ArrangeAsync(new SequenceDiceRoller(5), essence: 12);
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Rare);

        var result = await harness.Forge.ImbueAsync(harness.UserId, item.Id, default);

        Assert.True(result.Ok);
        Assert.Equal(AffixCatalog.Masterwork, result.Value!.Item.PrefixKey);
        Assert.Equal(12, result.Value.EssenceSpent);
        Assert.Equal(0, result.Value.Essence);
        Assert.Equal(0, (await CharacterAsync(harness)).Essence);
    }

    [Fact]
    public async Task Imbuing_a_second_word_fills_the_kind_that_is_empty()
    {
        // A two-slot item carrying a prefix must roll a suffix. Rolling the combined pool here
        // would sometimes hand back a prefix and quietly overwrite the one already paid for.
        var harness = await ArrangeAsync(new SequenceDiceRoller(2), essence: 30);
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Epic, prefix: AffixCatalog.Keen);

        var result = await harness.Forge.ImbueAsync(harness.UserId, item.Id, default);

        Assert.True(result.Ok);
        Assert.Equal(AffixCatalog.Keen, result.Value!.Item.PrefixKey);
        Assert.Equal(AffixCatalog.OfTheFox, result.Value.Item.SuffixKey);
        Assert.Equal("Keen Silvered Blade of the Fox", result.Value.Item.DisplayName);
    }

    [Fact]
    public async Task Essence_can_never_buy_a_word_above_the_items_rarity()
    {
        // The pool is the same one a drop uses, so MinimumRarity is honoured identically and
        // paying more cannot reach further up the catalog.
        var harness = await ArrangeAsync(new SequenceDiceRoller(9), essence: 6);
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Uncommon);

        var result = await harness.Forge.ImbueAsync(harness.UserId, item.Id, default);

        Assert.True(result.Ok);

        var rolled = result.Value!.Item;
        var word = rolled.PrefixKey ?? rolled.SuffixKey;

        Assert.NotNull(word);
        Assert.True(AffixCatalog.Find(word)!.MinimumRarity <= Rarity.Uncommon, word);
    }

    [Fact]
    public async Task Imbuing_a_common_item_is_refused()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(), essence: 500);
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Common);

        var result = await harness.Forge.ImbueAsync(harness.UserId, item.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.CannotUpgrade, result.Failure);
        Assert.Equal(500, (await CharacterAsync(harness)).Essence);
    }

    [Fact]
    public async Task Imbuing_a_consumable_is_refused()
    {
        // A Common item and a consumable land on the same arm, because both roll zero slots.
        var harness = await ArrangeAsync(new SequenceDiceRoller(), essence: 500);

        var potion = new InventoryItem
        {
            UserId = harness.UserId,
            ItemKey = "a-consumable-from-a-later-phase",
            Slot = ItemSlot.Consumable,
            Rarity = Rarity.Legendary
        };

        harness.Db.InventoryItems.Add(potion);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Forge.ImbueAsync(harness.UserId, potion.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.CannotUpgrade, result.Failure);
    }

    [Fact]
    public async Task Imbuing_a_full_item_is_refused()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(), essence: 500);
        var item = await GiveAsync(
            harness, ItemCatalog.SilveredBlade, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheFox);

        var result = await harness.Forge.ImbueAsync(harness.UserId, item.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.CannotUpgrade, result.Failure);
        Assert.Equal(500, (await CharacterAsync(harness)).Essence);
    }

    [Fact]
    public async Task Crafting_without_the_essence_is_refused_before_a_die_is_spent()
    {
        // The check runs ahead of the roll on purpose. A refused craft that had already spent
        // a die would make the outcome of the next paid one depend on how often you failed.
        var roller = new SequenceDiceRoller();
        var harness = await ArrangeAsync(roller, essence: 5);
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Rare);

        var result = await harness.Forge.ImbueAsync(harness.UserId, item.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.NotEnoughEssence, result.Failure);
        Assert.Contains("12", result.Message);
        Assert.Equal(0, roller.RollCount);
        Assert.Equal(5, (await CharacterAsync(harness)).Essence);
        Assert.Null((await harness.Db.InventoryItems.SingleAsync(i => i.Id == item.Id)).PrefixKey);
    }

    [Fact]
    public async Task A_retired_item_key_cannot_be_imbued()
    {
        // Salvage pays a retired key the floor so the row is not stranded forever, but crafting
        // has to refuse it: the sheet skips a retired definition before it ever reads the affix,
        // so the word would be paid for and then do nothing on the character it was bought for.
        // The upgrade bench already makes this ruling before it takes gold.
        var roller = new SequenceDiceRoller();
        var harness = await ArrangeAsync(roller, essence: 500);
        var item = await GiveAsync(harness, "axe-of-a-forgotten-patch", Rarity.Epic);

        var result = await harness.Forge.ImbueAsync(harness.UserId, item.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.CannotUpgrade, result.Failure);
        Assert.Equal(0, roller.RollCount);
        Assert.Equal(500, (await CharacterAsync(harness)).Essence);
        Assert.Null((await harness.Db.InventoryItems.SingleAsync(i => i.Id == item.Id)).PrefixKey);
    }

    [Fact]
    public async Task A_retired_item_key_cannot_be_reforged()
    {
        var roller = new SequenceDiceRoller();
        var harness = await ArrangeAsync(roller, essence: 500);
        var item = await GiveAsync(harness, "axe-of-a-forgotten-patch", Rarity.Epic, prefix: AffixCatalog.Keen);

        var result = await harness.Forge.ReforgeAsync(harness.UserId, item.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.CannotUpgrade, result.Failure);
        Assert.Equal(0, roller.RollCount);
        Assert.Equal(500, (await CharacterAsync(harness)).Essence);
        Assert.Equal(
            AffixCatalog.Keen,
            (await harness.Db.InventoryItems.SingleAsync(i => i.Id == item.Id)).PrefixKey);
    }

    [Fact]
    public async Task Another_users_item_cannot_be_imbued()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(), essence: 500);
        var thief = await postgres.CreateUserAsync("test|thief");

        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Epic);

        var result = await harness.Forge.ImbueAsync(thief.Id, item.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.NotFound, result.Failure);
    }

    // ---------------------------------------------------------------- reforge

    [Fact]
    public async Task Reforging_never_returns_the_same_word()
    {
        // The excluded word is dropped from the pool, so the four remaining Rare prefixes are
        // what face four reaches.
        var harness = await ArrangeAsync(new SequenceDiceRoller(4), essence: 24);
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Rare, prefix: AffixCatalog.Keen);

        var result = await harness.Forge.ReforgeAsync(harness.UserId, item.Id, default);

        Assert.True(result.Ok);
        Assert.NotEqual(AffixCatalog.Keen, result.Value!.Item.PrefixKey);
        Assert.Equal(AffixCatalog.Masterwork, result.Value.Item.PrefixKey);
        Assert.Equal(24, result.Value.EssenceSpent);
        Assert.Equal(0, result.Value.Essence);
    }

    [Fact]
    public async Task Reforging_rerolls_every_word_in_force()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(1, 1), essence: 60);
        var item = await GiveAsync(
            harness, ItemCatalog.SilveredBlade, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheFox);

        var result = await harness.Forge.ReforgeAsync(harness.UserId, item.Id, default);

        Assert.True(result.Ok);
        Assert.Equal(AffixCatalog.Balanced, result.Value!.Item.PrefixKey);
        Assert.Equal(AffixCatalog.OfTheBear, result.Value.Item.SuffixKey);
        Assert.Equal(60, result.Value.EssenceSpent);
    }

    [Fact]
    public async Task Reforging_leaves_an_empty_slot_empty()
    {
        // Filling one is what imbue is for, and it is half the price.
        var roller = new SequenceDiceRoller(1);
        var harness = await ArrangeAsync(roller, essence: 60);
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Epic, prefix: AffixCatalog.Keen);

        var result = await harness.Forge.ReforgeAsync(harness.UserId, item.Id, default);

        Assert.True(result.Ok);
        Assert.Null(result.Value!.Item.SuffixKey);
        Assert.Equal(1, roller.RollCount);
    }

    [Fact]
    public async Task Reforging_an_unaffixed_item_is_refused()
    {
        var roller = new SequenceDiceRoller();
        var harness = await ArrangeAsync(roller, essence: 500);
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Epic);

        var result = await harness.Forge.ReforgeAsync(harness.UserId, item.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.CannotUpgrade, result.Failure);
        Assert.Equal(0, roller.RollCount);
        Assert.Equal(500, (await CharacterAsync(harness)).Essence);
    }

    [Fact]
    public async Task Reforging_without_the_essence_is_refused()
    {
        var roller = new SequenceDiceRoller();
        var harness = await ArrangeAsync(roller, essence: 23);
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Rare, prefix: AffixCatalog.Keen);

        var result = await harness.Forge.ReforgeAsync(harness.UserId, item.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.NotEnoughEssence, result.Failure);
        Assert.Equal(0, roller.RollCount);
        Assert.Equal(AffixCatalog.Keen, (await harness.Db.InventoryItems.SingleAsync(i => i.Id == item.Id)).PrefixKey);
    }

    [Fact]
    public async Task Another_users_item_cannot_be_reforged()
    {
        var harness = await ArrangeAsync(new SequenceDiceRoller(), essence: 500);
        var thief = await postgres.CreateUserAsync("test|thief");

        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Epic, prefix: AffixCatalog.Keen);

        var result = await harness.Forge.ReforgeAsync(thief.Id, item.Id, default);

        Assert.False(result.Ok);
        Assert.Equal(RpgFailure.NotFound, result.Failure);
    }

    // ------------------------------------------------------- the balance race

    /// <summary>
    /// A second request already holding its own read of the character row, which is what makes
    /// a lost update reachable: EF keeps the values it first tracked, so this context's write
    /// carries the row version it saw rather than the one the winner left behind.
    /// </summary>
    private async Task<ForgeService> RivalAsync(Harness harness, TodoDbContext db, IDiceRoller roller)
    {
        await db.Characters.SingleAsync(c => c.UserId == harness.UserId);

        return new ForgeService(db, roller);
    }

    [Fact]
    public async Task Two_imbues_racing_on_one_balance_cannot_buy_two_words_for_one_payment()
    {
        // Exactly 30 essence and an Epic item with both slots empty. Both requests read 30,
        // both pass the affordability check, and both write 0: without a row version that is
        // one payment and two words. xmin is the token, so the loser rolls back whole.
        var harness = await ArrangeAsync(new SequenceDiceRoller(1), essence: ForgeRules.ImbueCost(Rarity.Epic));
        var item = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Epic);

        await using var second = postgres.CreateContext();
        var rival = await RivalAsync(harness, second, new SequenceDiceRoller(1));

        Assert.True((await harness.Forge.ImbueAsync(harness.UserId, item.Id, default)).Ok);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => rival.ImbueAsync(harness.UserId, item.Id, default));

        var after = await harness.Db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id);

        Assert.Equal(0, (await CharacterAsync(harness)).Essence);
        Assert.Equal(1, AffixRules.CountInForce(after));
    }

    [Fact]
    public async Task Two_salvages_racing_cannot_lose_the_essence_of_one_of_them()
    {
        // The mirror image: both read 0 and one writes 5 over the other's 12. The loser's item
        // survives with it, because the delete and the credit are the same transaction.
        var harness = await ArrangeAsync(new FixedDiceRoller(1));

        var first = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Epic);
        var secondItem = await GiveAsync(harness, ItemCatalog.GreatAxe, Rarity.Rare);

        await using var second = postgres.CreateContext();
        var rival = await RivalAsync(harness, second, new FixedDiceRoller(1));

        Assert.True((await harness.Forge.SalvageAsync(harness.UserId, first.Id, default)).Ok);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => rival.SalvageAsync(harness.UserId, secondItem.Id, default));

        Assert.Equal(12, (await CharacterAsync(harness)).Essence);
        Assert.NotNull(await harness.Db.InventoryItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == secondItem.Id));
    }

    // ------------------------------------------------------------- the sheet

    /// <summary>
    /// Equipping over an occupied slot, in one call, with nothing taken off first.
    /// </summary>
    /// <remarks>
    /// The path a player actually takes when they find better armour, and the one nothing
    /// covered. <c>RpgEndpointTests.Equipping_swaps_the_slot_and_updates_the_sheet</c> takes the
    /// weapon off and puts the same weapon back on, which is the workaround rather than the
    /// feature: the slot is empty when its equip lands, so the only interesting moment never
    /// happens.
    /// <para>
    /// Swapped twice, and both directions asserted, because the hazard is asymmetric. Freeing
    /// the slot and taking it are two UPDATEs in one batch, and EF orders a batch by ascending
    /// key rather than by the order the assignments were written - <c>IsEquipped</c> is only
    /// the index's filter, not one of its columns, so nothing tells EF the two are related.
    /// Whichever way the two ids happen to compare, one of the swaps below writes the taking
    /// row first, and the partial unique index is checked per row rather than per batch.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Equipping_over_an_occupied_slot_swaps_without_taking_the_old_one_off()
    {
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var token = TestContext.Current.CancellationToken;

        var worn = await harness.Db.InventoryItems
            .SingleAsync(i => i.UserId == harness.UserId && i.Slot == ItemSlot.Armour && i.IsEquipped, token);

        var found = await GiveAsync(harness, ItemCatalog.ScaleMail, Rarity.Common);

        async Task AssertSwapsToAsync(Guid takingId, Guid freedId)
        {
            var result = await harness.Adventurer.EquipAsync(harness.UserId, takingId, token);

            Assert.True(result.Ok, result.Message);

            var rows = await harness.Db.InventoryItems.AsNoTracking()
                .Where(i => i.UserId == harness.UserId && i.Slot == ItemSlot.Armour)
                .ToListAsync(token);

            Assert.True(rows.Single(i => i.Id == takingId).IsEquipped);
            Assert.False(rows.Single(i => i.Id == freedId).IsEquipped);
            Assert.Single(rows, i => i.IsEquipped);
        }

        await AssertSwapsToAsync(found.Id, worn.Id);
        await AssertSwapsToAsync(worn.Id, found.Id);
    }

    [Fact]
    public async Task A_constitution_affix_moves_max_hit_points_and_taking_it_off_clamps_them_back()
    {
        // Equipment changes Constitution, which changes the maximum. ClampHitPointsAsync
        // already runs after every equip and unequip; asserted rather than assumed.
        var harness = await ArrangeAsync(new FixedDiceRoller(1));
        var charm = await GiveAsync(harness, ItemCatalog.LuckyCoin, Rarity.Epic, suffix: AffixCatalog.OfTheOx);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        var before = (await harness.Sheets.BuildAsync(character, default)).MaxHitPoints;

        Assert.True((await harness.Adventurer.EquipAsync(harness.UserId, charm.Id, default)).Ok);

        var worn = (await harness.Sheets.BuildAsync(character, default)).MaxHitPoints;
        Assert.True(worn > before);

        Assert.True((await harness.Adventurer.UnequipAsync(harness.UserId, charm.Id, default)).Ok);

        var after = await CharacterAsync(harness);

        Assert.Equal(before, (await harness.Sheets.BuildAsync(character, default)).MaxHitPoints);
        Assert.True(after.CurrentHitPoints <= before);
    }

    [Fact]
    public async Task Wearing_a_set_is_the_only_thing_that_records_it()
    {
        // No ActiveSetKey column and no IsSetComplete flag: equipping is the whole write, and
        // the sheet derives the rest from the rows it already had to load.
        var harness = await ArrangeAsync(new FixedDiceRoller(1));

        var bow = await GiveAsync(harness, ItemCatalog.LongbowOfTheVale, Rarity.Common);
        var armour = await GiveAsync(harness, ItemCatalog.LeatherArmour, Rarity.Common);
        var boots = await GiveAsync(harness, ItemCatalog.BootsOfSpeed, Rarity.Common);

        foreach (var piece in new[] { bow, armour, boots })
        {
            Assert.True((await harness.Adventurer.EquipAsync(harness.UserId, piece.Id, default)).Ok);
        }

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        var (complete, equipped) = await harness.Sheets.BuildWithEquipmentAsync(character, default);

        var valewarden = SetCatalog.ProgressFor(equipped).Single(p => p.Set.Key == SetCatalog.Valewarden);

        Assert.Equal(3, valewarden.Equipped);
        Assert.Equal(2, valewarden.Active.Count);
        Assert.Equal(1, complete.GearAttackBonus);

        Assert.True((await harness.Adventurer.UnequipAsync(harness.UserId, boots.Id, default)).Ok);

        var (broken, stillWorn) = await harness.Sheets.BuildWithEquipmentAsync(character, default);

        Assert.Equal(2, SetCatalog.ProgressFor(stillWorn).Single(p => p.Set.Key == SetCatalog.Valewarden).Equipped);
        Assert.Equal(0, broken.GearAttackBonus);
        Assert.Equal(complete.ArmourClass, broken.ArmourClass);
    }

    // -------------------------------------------------------- the invariant

    [Fact]
    public async Task Nothing_in_the_forge_can_move_experience()
    {
        // The guarantee the whole design rests on (DEC-012), re-asserted against every new
        // way to spend a currency.
        var harness = await ArrangeAsync(new SequenceDiceRoller(1, 1, 1, 1, 1), essence: 500);

        var before = await CharacterAsync(harness);

        var scrap = await GiveAsync(
            harness, ItemCatalog.GreatAxe, Rarity.Epic, AffixCatalog.Keen, AffixCatalog.OfTheFox);
        var work = await GiveAsync(harness, ItemCatalog.SilveredBlade, Rarity.Epic);

        Assert.True((await harness.Forge.SalvageAsync(harness.UserId, scrap.Id, default)).Ok);
        Assert.True((await harness.Forge.ImbueAsync(harness.UserId, work.Id, default)).Ok);
        Assert.True((await harness.Forge.ImbueAsync(harness.UserId, work.Id, default)).Ok);
        Assert.True((await harness.Forge.ReforgeAsync(harness.UserId, work.Id, default)).Ok);

        var after = await CharacterAsync(harness);

        // Essence moved, and it is the only thing that did.
        Assert.NotEqual(before.Essence, after.Essence);
        Assert.Equal(before.TotalXp, after.TotalXp);
        Assert.Equal(before.TasksCompleted, after.TasksCompleted);
        Assert.Equal(LevelCurve.LevelForXp(before.TotalXp), LevelCurve.LevelForXp(after.TotalXp));
    }
}
