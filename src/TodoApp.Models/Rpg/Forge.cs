namespace TodoApp.Models.Rpg;

/// <summary>
/// The essence economy: what breaking an item pays, and what putting a word on one costs.
/// </summary>
/// <remarks>
/// Pure functions of rarity and the affixes in force, so the service that spends essence and
/// the tests that police the curve read the same numbers. Essence is the only material, on
/// purpose: four per-rarity materials would be four balance knobs and four columns for depth
/// that one cost curve already expresses.
/// </remarks>
public static class ForgeRules
{
    /// <summary>
    /// Yield tracks rarity and affixes, never BaseValue. Pricing it off BaseValue would make
    /// the shop a converter: buy the cheapest item of a tier, break it, repeat.
    /// </summary>
    public static int EssenceFor(Rarity rarity, int affixesInForce) =>
        rarity switch
        {
            Rarity.Common => 1,
            Rarity.Uncommon => 2,
            Rarity.Rare => 5,
            Rarity.Epic => 12,
            Rarity.Legendary => 30,
            _ => 1
        } + (2 * affixesInForce);

    /// <summary>
    /// What this item is worth broken down, affixes included. An item whose key has left the
    /// catalog still pays, at the floor, mirroring the sell path: a retired key must not
    /// strand a row in someone's bag forever.
    /// </summary>
    public static int EssenceFor(InventoryItem item) =>
        item.Definition is null ? 1 : EssenceFor(item.Rarity, AffixRules.CountInForce(item));

    /// <summary>Common holds no affixes, so it has no price.</summary>
    public static int ImbueCost(Rarity rarity) => rarity switch
    {
        Rarity.Uncommon => 6,
        Rarity.Rare => 12,
        Rarity.Epic => 30,
        Rarity.Legendary => 75,
        _ => 0
    };

    /// <summary>
    /// Rerolling everything costs twice what adding one word costs. Cost is a function of the
    /// target item's rarity, not of what was salvaged, so the price is legible on the item
    /// page before anything is spent.
    /// </summary>
    public static int ReforgeCost(Rarity rarity) => ImbueCost(rarity) * 2;

    /// <summary>
    /// The loop-closing invariant: a fully affixed item never pays for an affix on another
    /// item of its own rarity. Without this, break-and-imbue is a treadmill that turns
    /// inventory churn into power at no cost.
    /// </summary>
    /// <remarks>
    /// False at Common, where the question does not arise: a Common item holds no affixes, so
    /// there is nothing to buy and no treadmill to close.
    /// </remarks>
    public static bool PaysForItsOwnAffix(Rarity rarity) =>
        ImbueCost(rarity) > 0 && EssenceFor(rarity, MaxAffixes(rarity)) >= ImbueCost(rarity);

    /// <summary>The most affixes an item of this rarity can carry, in any equippable slot.</summary>
    public static int MaxAffixes(Rarity rarity) => AffixRules.RollableFor(ItemSlot.Weapon, rarity);
}
