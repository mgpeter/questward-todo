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
    /// Hands over a specific item. Rolls nothing, affixes included: starting gear and quest
    /// rewards are promises the catalog already made, and a die here would make the same
    /// promise pay out differently for two people who did the same work.
    /// </summary>
    public InventoryItem Grant(Guid userId, string itemKey, Rarity rarity)
    {
        var definition = ItemCatalog.Find(itemKey)
            ?? throw new InvalidOperationException($"'{itemKey}' is not in the item catalog.");

        var item = new InventoryItem
        {
            UserId = userId,
            ItemKey = definition.Key,
            Slot = definition.Slot,
            Rarity = rarity,
            IsEquipped = false,
            AcquiredAt = DateTimeOffset.UtcNow
        };

        db.InventoryItems.Add(item);

        return item;
    }

    public int RollGold(MonsterDefinition monster, bool silverTongue)
    {
        var span = Math.Max(1, monster.MaxGold - monster.MinGold + 1);
        var gold = monster.MinGold + roller.Roll(span) - 1;

        // The Bard's Silver Tongue.
        return silverTongue ? gold + (gold / 2) : gold;
    }

    private LootEntry PickWeighted(IReadOnlyList<LootEntry> table)
    {
        var total = table.Sum(e => e.Weight);
        var roll = roller.Roll(total);

        var running = 0;

        foreach (var entry in table)
        {
            running += entry.Weight;

            if (roll <= running)
            {
                return entry;
            }
        }

        return table[^1];
    }
}
