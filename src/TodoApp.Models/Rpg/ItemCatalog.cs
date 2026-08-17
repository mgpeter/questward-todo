using TodoApp.Models.Dice;

namespace TodoApp.Models.Rpg;

public enum ItemSlot
{
    Weapon = 0,
    Armour = 1,
    Trinket = 2
}

public enum Rarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4
}

public static class RarityRules
{
    /// <summary>
    /// The bonus a rarity adds on top of the item's intrinsic power. Rarity is rolled per
    /// drop, so one catalog entry covers the whole range from junk to trophy.
    /// </summary>
    public static int BonusFor(Rarity rarity) => (int)rarity;

    public static int ValueMultiplier(Rarity rarity) => rarity switch
    {
        Rarity.Common => 1,
        Rarity.Uncommon => 3,
        Rarity.Rare => 8,
        Rarity.Epic => 20,
        Rarity.Legendary => 50,
        _ => 1
    };

    public static string Describe(Rarity rarity) => rarity.ToString().ToLowerInvariant();
}

/// <param name="BonusAbility">
/// Which ability the rarity bonus lands on. For a weapon this raises attack and damage
/// together, which is why a rare sword feels different from a common one.
/// </param>
public sealed record ItemDefinition(
    string Key,
    string Name,
    ItemSlot Slot,
    string Blurb,
    string? DamageNotation = null,
    bool Finesse = false,
    int ArmourBonus = 0,
    Ability? BonusAbility = null,
    int BaseValue = 10)
{
    public DiceExpression? Damage =>
        DamageNotation is null ? null : DiceExpression.Parse(DamageNotation);

    public AbilityScores AbilityBonusesAt(Rarity rarity)
    {
        var bonus = RarityRules.BonusFor(rarity);

        return BonusAbility is null || bonus == 0 ? Zero : Zero.With(BonusAbility.Value, bonus);
    }

    public int ArmourBonusAt(Rarity rarity) =>
        Slot == ItemSlot.Armour ? ArmourBonus + RarityRules.BonusFor(rarity) : ArmourBonus;

    public int ValueAt(Rarity rarity) => BaseValue * RarityRules.ValueMultiplier(rarity);

    /// <summary>All six bonuses at zero, the additive identity for ability bonuses.</summary>
    public static AbilityScores Zero => AbilityScores.Zero;
}

/// <summary>Code-held, following DEC-004. Only the key and rolled rarity are persisted.</summary>
public static class ItemCatalog
{
    // Starting gear
    public const string RustyLongsword = "rusty-longsword";
    public const string WornDagger = "worn-dagger";
    public const string CrackedQuarterstaff = "cracked-quarterstaff";
    public const string PlainMace = "plain-mace";
    public const string HuntingBow = "hunting-bow";
    public const string LeatherArmour = "leather-armour";
    public const string ChainShirt = "chain-shirt";
    public const string TravellersRobes = "travellers-robes";

    // Drops
    public const string GoblinCleaver = "goblin-cleaver";
    public const string SilveredBlade = "silvered-blade";
    public const string WardingShield = "warding-shield";
    public const string ScaleMail = "scale-mail";
    public const string BootsOfSpeed = "boots-of-speed";
    public const string AmuletOfInsight = "amulet-of-insight";
    public const string RingOfVigour = "ring-of-vigour";
    public const string CharmOfPresence = "charm-of-presence";
    public const string DragonfangSpear = "dragonfang-spear";

    public static IReadOnlyList<ItemDefinition> All { get; } =
    [
        // --- Weapons ---------------------------------------------------------
        new(RustyLongsword, "Rusty Longsword", ItemSlot.Weapon,
            "Serviceable, if you do not look too closely at the edge.",
            DamageNotation: "1d8", BonusAbility: Ability.Strength, BaseValue: 12),

        new(WornDagger, "Worn Dagger", ItemSlot.Weapon,
            "Small, quick, and honest about what it is.",
            DamageNotation: "1d4", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 8),

        new(CrackedQuarterstaff, "Cracked Quarterstaff", ItemSlot.Weapon,
            "More walking stick than weapon, but it swings.",
            DamageNotation: "1d6", BonusAbility: Ability.Intelligence, BaseValue: 8),

        new(PlainMace, "Plain Mace", ItemSlot.Weapon,
            "Blunt instrument, blunt purpose.",
            DamageNotation: "1d6", BonusAbility: Ability.Wisdom, BaseValue: 10),

        new(HuntingBow, "Hunting Bow", ItemSlot.Weapon,
            "Draws smoothly. Smells faintly of pine.",
            DamageNotation: "1d8", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 14),

        new(GoblinCleaver, "Goblin Cleaver", ItemSlot.Weapon,
            "Notched from use rather than neglect.",
            DamageNotation: "1d8", BonusAbility: Ability.Strength, BaseValue: 25),

        new(SilveredBlade, "Silvered Blade", ItemSlot.Weapon,
            "Cold to the touch, and quicker than it looks.",
            DamageNotation: "1d8", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 40),

        new(DragonfangSpear, "Dragonfang Spear", ItemSlot.Weapon,
            "The tooth is real. Nobody asks where it came from.",
            DamageNotation: "1d10", BonusAbility: Ability.Strength, BaseValue: 90),

        // --- Armour ----------------------------------------------------------
        new(TravellersRobes, "Traveller's Robes", ItemSlot.Armour,
            "Comfortable. Not, strictly speaking, protective.",
            ArmourBonus: 1, BonusAbility: Ability.Intelligence, BaseValue: 8),

        new(LeatherArmour, "Leather Armour", ItemSlot.Armour,
            "Quiet, flexible, and better than nothing.",
            ArmourBonus: 2, BonusAbility: Ability.Dexterity, BaseValue: 12),

        new(ChainShirt, "Chain Shirt", ItemSlot.Armour,
            "Heavy on the shoulders, reassuring everywhere else.",
            ArmourBonus: 3, BonusAbility: Ability.Constitution, BaseValue: 20),

        new(ScaleMail, "Scale Mail", ItemSlot.Armour,
            "Overlapping plates that rattle when you run.",
            ArmourBonus: 4, BonusAbility: Ability.Constitution, BaseValue: 45),

        new(WardingShield, "Warding Shield", ItemSlot.Armour,
            "Someone painted a sigil on it. It may even help.",
            ArmourBonus: 5, BonusAbility: Ability.Wisdom, BaseValue: 70),

        // --- Trinkets ---------------------------------------------------------
        new(BootsOfSpeed, "Boots of Speed", ItemSlot.Trinket,
            "You arrive slightly before you expect to.",
            BonusAbility: Ability.Dexterity, BaseValue: 30),

        new(AmuletOfInsight, "Amulet of Insight", ItemSlot.Trinket,
            "Problems look smaller while you wear it.",
            BonusAbility: Ability.Intelligence, BaseValue: 30),

        new(RingOfVigour, "Ring of Vigour", ItemSlot.Trinket,
            "A steady warmth, somewhere behind the sternum.",
            BonusAbility: Ability.Constitution, BaseValue: 35),

        new(CharmOfPresence, "Charm of Presence", ItemSlot.Trinket,
            "People finish their sentences more generously around you.",
            BonusAbility: Ability.Charisma, BaseValue: 30)
    ];

    private static readonly Dictionary<string, ItemDefinition> ByKey =
        All.ToDictionary(i => i.Key, StringComparer.Ordinal);

    public static ItemDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);
}
