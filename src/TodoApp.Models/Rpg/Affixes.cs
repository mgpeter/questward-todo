using TodoApp.Models.Dice;

namespace TodoApp.Models.Rpg;

public enum AffixKind
{
    Prefix = 0,
    Suffix = 1
}

/// <summary>What an affix or a set bonus contributes, in the only five currencies the sheet can spend.</summary>
public sealed record BonusEffects(
    AbilityScores Abilities,
    int ArmourBonus,
    int AttackBonus,
    int DamageBonus,
    int CriticalRangeBonus)
{
    public static BonusEffects None { get; } = new(AbilityScores.Zero, 0, 0, 0, 0);

    public BonusEffects Plus(BonusEffects other) => new(
        Abilities.Plus(other.Abilities),
        ArmourBonus + other.ArmourBonus,
        AttackBonus + other.AttackBonus,
        DamageBonus + other.DamageBonus,
        CriticalRangeBonus + other.CriticalRangeBonus);

    /// <summary>True when this contributes nothing, so callers can skip an empty label.</summary>
    public bool IsNothing => this == None;
}

/// <param name="MinimumRarity">
/// A roll-time filter only, checked in <see cref="AffixRules.EligibleFor"/> and therefore in
/// both dropping and crafting. Deliberately not re-checked on read, so upgrading a Rare Keen
/// weapon to Epic keeps its Keen.
/// </param>
/// <param name="Slots">Null means every slot that can hold an affix at all.</param>
/// <param name="CriticalRange">
/// Flat, never scaled by tier. Critical range is the one lever where a second point is worth
/// far more than the first, so tiering it would make Epic weapons crit on a coin toss.
/// </param>
public sealed record AffixDefinition(
    string Key,
    string Word,
    AffixKind Kind,
    string Blurb,
    Rarity MinimumRarity = Rarity.Uncommon,
    IReadOnlyList<ItemSlot>? Slots = null,
    Ability? Ability = null,
    Ability? SecondAbility = null,
    int ArmourPerTier = 0,
    int AttackPerTier = 0,
    int DamagePerTier = 0,
    int CriticalRange = 0)
{
    public bool FitsOn(ItemSlot slot) => Slots is null || Slots.Contains(slot);

    public BonusEffects EffectAt(Rarity rarity)
    {
        var tier = AffixRules.TierAt(rarity);
        var abilities = AbilityScores.Zero;

        if (Ability is { } first)
        {
            abilities = abilities.Plus(first, tier);
        }

        if (SecondAbility is { } second)
        {
            abilities = abilities.Plus(second, tier);
        }

        return new BonusEffects(
            abilities,
            ArmourPerTier * tier,
            AttackPerTier * tier,
            DamagePerTier * tier,
            CriticalRange);
    }
}

/// <summary>
/// Code-held, following DEC-004: only the rolled key is persisted, so retuning an affix or
/// retiring one ships without a migration.
/// </summary>
/// <remarks>
/// Prefixes never grant ability scores and suffixes only grant ability scores. That split is
/// what lets a name read like real gear rather than like a stat block.
/// <para>
/// Deliberately absent: a "Gilded" affix that raises an item's value. It has been proposed
/// before and will be again. A rolled affix that mints gold is an inflation source that skips
/// the stamina chain (DEC-014), which is the same bug as pricing salvage off BaseValue,
/// wearing a hat.
/// </para>
/// </remarks>
public static class AffixCatalog
{
    // Prefixes: how it fights.
    public const string Balanced = "balanced";
    public const string Vicious = "vicious";
    public const string Warded = "warded";
    public const string Keen = "keen";
    public const string Masterwork = "masterwork";

    // Suffixes: what it grants.
    public const string OfTheBear = "of-the-bear";
    public const string OfTheFox = "of-the-fox";
    public const string OfTheOx = "of-the-ox";
    public const string OfTheOwl = "of-the-owl";
    public const string OfTheOracle = "of-the-oracle";
    public const string OfTheSiren = "of-the-siren";
    public const string OfTheTitan = "of-the-titan";
    public const string OfTheMagus = "of-the-magus";

    /// <summary>The slots a weapon-bound prefix may land on.</summary>
    private static readonly IReadOnlyList<ItemSlot> WeaponOnly = [ItemSlot.Weapon];

    public static IReadOnlyList<AffixDefinition> All { get; } =
    [
        // --- Prefixes ---------------------------------------------------------
        new(Balanced, "Balanced", AffixKind.Prefix,
            "It comes back to guard on its own.",
            AttackPerTier: 1),

        new(Vicious, "Vicious", AffixKind.Prefix,
            "Every cut opens wider than it has any right to.",
            Slots: WeaponOnly, DamagePerTier: 1),

        new(Warded, "Warded", AffixKind.Prefix,
            "Blows land a finger's width off.",
            ArmourPerTier: 1),

        new(Keen, "Keen", AffixKind.Prefix,
            "The edge finds the seam without being asked.",
            MinimumRarity: Rarity.Rare, Slots: WeaponOnly, CriticalRange: 1),

        new(Masterwork, "Masterwork", AffixKind.Prefix,
            "Made once, properly, by someone who signed it.",
            MinimumRarity: Rarity.Rare, AttackPerTier: 1, ArmourPerTier: 1),

        // --- Suffixes ---------------------------------------------------------
        new(OfTheBear, "of the Bear", AffixKind.Suffix,
            "You lift what you used to drag.",
            Ability: Rpg.Ability.Strength),

        new(OfTheFox, "of the Fox", AffixKind.Suffix,
            "Your hands arrive before you decide to move them.",
            Ability: Rpg.Ability.Dexterity),

        new(OfTheOx, "of the Ox", AffixKind.Suffix,
            "You tire an hour later than you used to.",
            Ability: Rpg.Ability.Constitution),

        new(OfTheOwl, "of the Owl", AffixKind.Suffix,
            "Details you would have missed queue up politely.",
            Ability: Rpg.Ability.Intelligence),

        new(OfTheOracle, "of the Oracle", AffixKind.Suffix,
            "You notice the thing that was about to go wrong.",
            Ability: Rpg.Ability.Wisdom),

        new(OfTheSiren, "of the Siren", AffixKind.Suffix,
            "People agree with you slightly too easily.",
            Ability: Rpg.Ability.Charisma),

        new(OfTheTitan, "of the Titan", AffixKind.Suffix,
            "Old giants' work, and it remembers being large.",
            MinimumRarity: Rarity.Rare,
            Ability: Rpg.Ability.Strength, SecondAbility: Rpg.Ability.Constitution),

        new(OfTheMagus, "of the Magus", AffixKind.Suffix,
            "The theory and the instinct arrive together.",
            MinimumRarity: Rarity.Rare,
            Ability: Rpg.Ability.Intelligence, SecondAbility: Rpg.Ability.Wisdom)
    ];

    private static readonly Dictionary<string, AffixDefinition> ByKey =
        All.ToDictionary(a => a.Key, StringComparer.Ordinal);

    public static AffixDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);
}

/// <summary>
/// Every affix question answered as a pure function of slot, rarity and the two stored keys.
/// Nothing here reads or writes state, which is what lets the same helper serve a drop, an
/// imbue, a reforge and the read side of the sheet.
/// </summary>
public static class AffixRules
{
    /// <summary>A natural 17 crit would make the arithmetic of an attack roll pointless.</summary>
    public const int MinimumCriticalOn = 18;

    /// <summary>
    /// How many affixes a drop of this rarity carries. Deterministic rather than "usually
    /// none" at Common: half of all drops are Common, and a variable slot count there would
    /// shift every seeded dice script in the suite for no gameplay gain.
    /// </summary>
    public static int RollableFor(ItemSlot slot, Rarity rarity) => slot switch
    {
        // A consumable is used up rather than worn, so there is nothing for a word to sit on.
        // An explicit arm rather than falling through a default, because the ruling is intent.
        ItemSlot.Consumable => 0,
        _ => rarity switch
        {
            Rarity.Uncommon or Rarity.Rare => 1,
            Rarity.Epic or Rarity.Legendary => 2,
            _ => 0
        }
    };

    /// <summary>The magnitude multiplier an affix is worth at this rarity.</summary>
    public static int TierAt(Rarity rarity) => rarity switch
    {
        Rarity.Uncommon or Rarity.Rare => 1,
        Rarity.Epic or Rarity.Legendary => 2,
        _ => 0
    };

    /// <summary>Both kinds, in catalog order, for the single roll that fills a one-slot item.</summary>
    public static IReadOnlyList<AffixDefinition> EligibleFor(ItemSlot slot, Rarity rarity) =>
        RollableFor(slot, rarity) == 0
            ? []
            : [.. AffixCatalog.All.Where(a => a.MinimumRarity <= rarity && a.FitsOn(slot))];

    public static IReadOnlyList<AffixDefinition> EligibleFor(ItemSlot slot, Rarity rarity, AffixKind kind) =>
        [.. EligibleFor(slot, rarity).Where(a => a.Kind == kind)];

    /// <summary>
    /// Rolls the affixes for a fresh drop. Zero slots costs zero dice, which is what keeps
    /// every existing seeded script intact: a Common drop consumes no extra rolls at all.
    /// </summary>
    /// <remarks>
    /// At one slot this is a single roll over the combined pool, assigned by the winner's own
    /// kind, so both kinds stay reachable for one die. At two slots it is one roll for the
    /// prefix and then one for the suffix, in that order.
    /// </remarks>
    public static (AffixDefinition? Prefix, AffixDefinition? Suffix) Roll(
        ItemSlot slot,
        Rarity rarity,
        IDiceRoller roller)
    {
        var slots = RollableFor(slot, rarity);

        if (slots == 0)
        {
            return (null, null);
        }

        if (slots == 1)
        {
            var rolled = RollOne(slot, rarity, kind: null, roller);

            return rolled is { Kind: AffixKind.Prefix } ? (rolled, null) : (null, rolled);
        }

        var prefix = RollOne(slot, rarity, AffixKind.Prefix, roller);
        var suffix = RollOne(slot, rarity, AffixKind.Suffix, roller);

        return (prefix, suffix);
    }

    /// <summary>
    /// One affix from the eligible pool, or null when the pool is empty. A null kind rolls
    /// over both kinds at once, which is the drop's one-slot case and the imbue's
    /// both-slots-empty case sharing a single die.
    /// </summary>
    /// <param name="excluding">
    /// The word already in the slot, for a reforge. Paying essence must never hand back the
    /// word you were paying to be rid of.
    /// </param>
    public static AffixDefinition? RollOne(
        ItemSlot slot,
        Rarity rarity,
        AffixKind? kind,
        IDiceRoller roller,
        string? excluding = null)
    {
        var pool = kind is { } wanted ? EligibleFor(slot, rarity, wanted) : EligibleFor(slot, rarity);

        if (excluding is not null)
        {
            pool = [.. pool.Where(a => !string.Equals(a.Key, excluding, StringComparison.Ordinal))];
        }

        return pool.Count == 0 ? null : pool[roller.Roll(pool.Count) - 1];
    }

    /// <summary>
    /// The affixes a stored item actually carries: resolved, with anything the catalog no
    /// longer knows dropped, and truncated to what its rarity permits.
    /// </summary>
    /// <remarks>
    /// The truncation is defensive rather than reachable. A stored pair must never out-perform
    /// the rarity printed on the label, whatever wrote the row.
    /// </remarks>
    public static (AffixDefinition? Prefix, AffixDefinition? Suffix) InForce(InventoryItem item)
    {
        var prefix = AffixCatalog.Find(item.PrefixKey);
        var suffix = AffixCatalog.Find(item.SuffixKey);

        // A retired key reads as nothing, the same way a retired item key and a retired badge
        // key do, rather than crashing the sheet of whoever happened to be holding one.
        if (prefix is { Kind: not AffixKind.Prefix })
        {
            prefix = null;
        }

        if (suffix is { Kind: not AffixKind.Suffix })
        {
            suffix = null;
        }

        return RollableFor(item.Slot, item.Rarity) switch
        {
            0 => (null, null),
            1 => prefix is not null ? (prefix, null) : (null, suffix),
            _ => (prefix, suffix)
        };
    }

    /// <summary>How many words are actually working, which is what salvage pays for.</summary>
    public static int CountInForce(InventoryItem item)
    {
        var (prefix, suffix) = InForce(item);

        return (prefix is null ? 0 : 1) + (suffix is null ? 0 : 1);
    }

    public static BonusEffects EffectsOf(InventoryItem item) => EffectsOf(item, item.Rarity);

    /// <summary>
    /// The words this item carries, valued at <paramref name="rarity"/> rather than at its own.
    /// </summary>
    /// <remarks>
    /// The override exists for the upgrade preview, which has to answer what the same two words
    /// would be worth one rarity up. Which words are in force is still read at the item's current
    /// rarity: an upgrade never rolls a new one, so a slot the step opens stays empty until the
    /// forge fills it.
    /// </remarks>
    public static BonusEffects EffectsOf(InventoryItem item, Rarity rarity)
    {
        var (prefix, suffix) = InForce(item);

        if (prefix is null && suffix is null)
        {
            return BonusEffects.None;
        }

        var effects = prefix?.EffectAt(rarity) ?? BonusEffects.None;

        return suffix is null ? effects : effects.Plus(suffix.EffectAt(rarity));
    }

    /// <summary>
    /// The only place a name reaches a player. Every producer in the app routes through here,
    /// because a producer that composes the catalog name itself reads to the player as the
    /// affix they rolled having been silently lost.
    /// </summary>
    public static string DisplayName(InventoryItem item)
    {
        var (prefix, suffix) = InForce(item);

        return Compose(item.Definition?.Name ?? item.ItemKey, prefix, suffix);
    }

    public static string DisplayName(ItemDefinition definition, AffixDefinition? prefix, AffixDefinition? suffix) =>
        Compose(definition.Name, prefix, suffix);

    private static string Compose(string name, AffixDefinition? prefix, AffixDefinition? suffix) =>
        string.Join(' ', new[] { prefix?.Word, name, suffix?.Word }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
