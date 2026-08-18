using TodoApp.Data;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

/// <summary>
/// Rolls drops. Every roll goes through <see cref="IDiceRoller"/>, so loot is as testable
/// as combat.
/// </summary>
public sealed class LootService(TodoDbContext db, IDiceRoller roller)
{
    /// <summary>
    /// Cumulative d100 thresholds. Legendary is a single face, which is what makes one
    /// worth talking about.
    /// </summary>
    private static readonly (int UpTo, Rarity Rarity)[] RarityTable =
    [
        (50, Rarity.Common),
        (80, Rarity.Uncommon),
        (93, Rarity.Rare),
        (99, Rarity.Epic),
        (100, Rarity.Legendary)
    ];

    public Rarity RollRarity(bool advantage)
    {
        var roll = roller.Roll(100);

        if (advantage)
        {
            // The Ranger's Favoured Quarry: roll twice, keep the better result.
            roll = Math.Max(roll, roller.Roll(100));
        }

        foreach (var (upTo, rarity) in RarityTable)
        {
            if (roll <= upTo)
            {
                return rarity;
            }
        }

        return Rarity.Common;
    }

    /// <summary>Rolls the monster's table, returning null when nothing drops.</summary>
    public InventoryItem? RollDrop(Guid userId, MonsterDefinition monster, bool rarityAdvantage)
    {
        if (monster.LootTable.Count == 0 || roller.Roll(100) > monster.DropChance)
        {
            return null;
        }

        var definition = PickWeighted(monster.LootTable);
        var item = ItemCatalog.Find(definition.ItemKey);

        if (item is null)
        {
            return null;
        }

        // Rarity first, then affixes, because the affix roll reads the rarity it just got.
        // A Common drop rolls zero slots and therefore spends no extra dice at all, which is
        // what keeps every seeded script in the suite intact.
        var rarity = RollRarity(rarityAdvantage);
        var (prefix, suffix) = AffixRules.Roll(item.Slot, rarity, roller);

        return new InventoryItem
        {
            UserId = userId,
            ItemKey = item.Key,
            Slot = item.Slot,
            Rarity = rarity,
            PrefixKey = prefix?.Key,
            SuffixKey = suffix?.Key,
            IsEquipped = false,
            AcquiredAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Rolls a guaranteed drop from a table that is not a monster's, floored at a rarity.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RollDrop"/> rather than a flag on it, because the two differ in
    /// the one thing that matters: this one always pays. A dungeon clear that could roll nothing
    /// would make the last room of a five room run indistinguishable from the first, and the
    /// whole price of the run is that it is five fights rather than one.
    /// <para>
    /// The dice, in order: one weighted pick over the table, then the rarity d100 (twice for a
    /// Ranger), then whatever the rarity's affix slots cost. No drop-chance d100 at all, which is
    /// the one roll <see cref="RollDrop"/> spends that this does not.
    /// </para>
    /// </remarks>
    public async Task<InventoryItem?> RollRewardAsync(
        Guid userId,
        IReadOnlyList<LootEntry> table,
        Rarity floor,
        bool rarityAdvantage,
        CancellationToken cancellationToken)
    {
        if (table.Count == 0)
        {
            return null;
        }

        var definition = ItemCatalog.Find(PickWeighted(table).ItemKey);

        if (definition is null)
        {
            return null;
        }

        // The floor lifts a poor roll and never caps a good one, so the deepest dungeon can still
        // hand back a Legendary and the shallowest can never hand back junk.
        var rarity = (Rarity)Math.Max((int)RollRarity(rarityAdvantage), (int)floor);
        var (prefix, suffix) = AffixRules.Roll(definition.Slot, rarity, roller);

        var item = await InventoryStack.AcquireAsync(db, userId, definition, rarity, cancellationToken);

        // Only when the slot rolls any at all. Acquiring can land on an existing stack, and the
        // one slot that stacks is the one slot that rolls no affixes, so writing unconditionally
        // would be writing null over null forever until the day it was not.
        if (AffixRules.RollableFor(definition.Slot, rarity) > 0)
        {
            item.PrefixKey = prefix?.Key;
            item.SuffixKey = suffix?.Key;
        }

        return item;
    }

    /// <summary>
    /// Hands over a specific item. Rolls nothing, affixes included: starting gear and quest
    /// rewards are promises the catalog already made, and a die here would make the same
    /// promise pay out differently for two people who did the same work.
    /// </summary>
    /// <remarks>
    /// Asynchronous since consumables began to stack. Handing over the second of a kind has to
    /// find the row the first one is on and add to it, and a bare insert beside it loses to the
    /// stacking index. The shop's purchase path shares the same helper for the same reason.
    /// </remarks>
    public Task<InventoryItem> GrantAsync(
        Guid userId,
        string itemKey,
        Rarity rarity,
        CancellationToken cancellationToken)
    {
        var definition = ItemCatalog.Find(itemKey)
            ?? throw new InvalidOperationException($"'{itemKey}' is not in the item catalog.");

        return InventoryStack.AcquireAsync(db, userId, definition, rarity, cancellationToken);
    }

    public int RollGold(MonsterDefinition monster, bool silverTongue)
    {
        var span = Math.Max(1, monster.MaxGold - monster.MinGold + 1);
        var gold = monster.MinGold + roller.Roll(span) - 1;

        // The Bard's Silver Tongue.
        return silverTongue ? gold + (gold / 2) : gold;
    }

    private LootEntry PickWeighted(IReadOnlyList<LootEntry> table) =>
        PickWeighted(table, e => e.Weight, roller);

    /// <summary>
    /// One weighted draw for one die, walked in declaration order.
    /// </summary>
    /// <remarks>
    /// Generic because a dungeon's room pool is the same draw over a different record, and the
    /// two must stay the same draw: one roll of the summed weight, resolved in the order the
    /// catalog declares. Two copies would drift, and the first thing to drift would be how many
    /// dice a pick costs, which is the one number the whole test suite is written against.
    /// </remarks>
    public static T PickWeighted<T>(IReadOnlyList<T> table, Func<T, int> weight, IDiceRoller roller)
    {
        var total = table.Sum(weight);
        var roll = roller.Roll(total);

        var running = 0;

        foreach (var entry in table)
        {
            running += weight(entry);

            if (roll <= running)
            {
                return entry;
            }
        }

        return table[^1];
    }
}
