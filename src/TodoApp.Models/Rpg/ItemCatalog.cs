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

    // Expanded gear
    public const string IronMace = "iron-mace";
    public const string OakenStaff = "oaken-staff";
    public const string DuellingRapier = "duelling-rapier";
    public const string GreatAxe = "great-axe";
    public const string RunedWand = "runed-wand";
    public const string LongbowOfTheVale = "longbow-of-the-vale";
    public const string ChoirmastersLute = "choirmasters-lute";
    public const string ReliquaryHammer = "reliquary-hammer";
    public const string PaddedJerkin = "padded-jerkin";
    public const string StuddedLeather = "studded-leather";
    public const string BreastplateOfDawn = "breastplate-of-dawn";
    public const string ShadowweaveCloak = "shadowweave-cloak";
    public const string RingOfFocus = "ring-of-focus";
    public const string PendantOfTheBear = "pendant-of-the-bear";
    public const string GlovesOfTheThief = "gloves-of-the-thief";
    public const string CircletOfClarity = "circlet-of-clarity";
    public const string LuckyCoin = "lucky-coin";

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
            BonusAbility: Ability.Charisma, BaseValue: 30),

        // --- Expanded weapons -------------------------------------------------
        new(IronMace, "Iron Mace", ItemSlot.Weapon,
            "No edge to dull, which is rather the point.",
            DamageNotation: "1d8", BonusAbility: Ability.Wisdom, BaseValue: 22),

        new(OakenStaff, "Oaken Staff", ItemSlot.Weapon,
            "Worn smooth where a hand has gripped it for years.",
            DamageNotation: "1d8", BonusAbility: Ability.Intelligence, BaseValue: 26),

        new(DuellingRapier, "Duelling Rapier", ItemSlot.Weapon,
            "Balanced for someone who intends to be precise about this.",
            DamageNotation: "1d8", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 35),

        new(GreatAxe, "Great Axe", ItemSlot.Weapon,
            "Two hands, one purpose.",
            DamageNotation: "1d12", BonusAbility: Ability.Strength, BaseValue: 55),

        new(RunedWand, "Runed Wand", ItemSlot.Weapon,
            "The runes shift when you are not looking directly at them.",
            DamageNotation: "1d6", Finesse: true, BonusAbility: Ability.Intelligence, BaseValue: 48),

        new(LongbowOfTheVale, "Longbow of the Vale", ItemSlot.Weapon,
            "Draws like it wants to be drawn.",
            DamageNotation: "1d10", Finesse: true, BonusAbility: Ability.Dexterity, BaseValue: 65),

        new(ChoirmastersLute, "Choirmaster's Lute", ItemSlot.Weapon,
            "Surprisingly solid. The strings are almost incidental.",
            DamageNotation: "1d6", Finesse: true, BonusAbility: Ability.Charisma, BaseValue: 40),

        new(ReliquaryHammer, "Reliquary Hammer", ItemSlot.Weapon,
            "Something small and holy rattles in the head of it.",
            DamageNotation: "1d10", BonusAbility: Ability.Wisdom, BaseValue: 72),

        // --- Expanded armour --------------------------------------------------
        new(PaddedJerkin, "Padded Jerkin", ItemSlot.Armour,
            "Warm, at least.",
            ArmourBonus: 1, BonusAbility: Ability.Constitution, BaseValue: 6),

        new(StuddedLeather, "Studded Leather", ItemSlot.Armour,
            "The studs are more useful than they look.",
            ArmourBonus: 3, BonusAbility: Ability.Dexterity, BaseValue: 28),

        new(BreastplateOfDawn, "Breastplate of Dawn", ItemSlot.Armour,
            "Catches the light even indoors, which is either holy or a nuisance.",
            ArmourBonus: 5, BonusAbility: Ability.Charisma, BaseValue: 80),

        new(ShadowweaveCloak, "Shadowweave Cloak", ItemSlot.Armour,
            "You keep losing track of your own sleeves.",
            ArmourBonus: 4, BonusAbility: Ability.Dexterity, BaseValue: 68),

        // --- Expanded trinkets -------------------------------------------------
        new(RingOfFocus, "Ring of Focus", ItemSlot.Trinket,
            "The noise recedes a little while you wear it.",
            BonusAbility: Ability.Wisdom, BaseValue: 32),

        new(PendantOfTheBear, "Pendant of the Bear", ItemSlot.Trinket,
            "Heavy, and you find you do not mind carrying it.",
            BonusAbility: Ability.Strength, BaseValue: 34),

        new(GlovesOfTheThief, "Gloves of the Thief", ItemSlot.Trinket,
            "Fingertips worn thin from honest work, allegedly.",
            BonusAbility: Ability.Dexterity, BaseValue: 38),

        new(CircletOfClarity, "Circlet of Clarity", ItemSlot.Trinket,
            "Thoughts arrive already in order.",
            BonusAbility: Ability.Intelligence, BaseValue: 42),

        new(LuckyCoin, "Lucky Coin", ItemSlot.Trinket,
            "It has come up heads every time so far. Every single time.",
            BonusAbility: Ability.Charisma, BaseValue: 26)
    ];

    private static readonly Dictionary<string, ItemDefinition> ByKey =
        All.ToDictionary(i => i.Key, StringComparer.Ordinal);

    public static ItemDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);
}
