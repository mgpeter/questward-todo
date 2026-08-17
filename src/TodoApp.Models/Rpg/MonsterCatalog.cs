using TodoApp.Models.Dice;

namespace TodoApp.Models.Rpg;

/// <param name="Weight">Relative chance within the table. Not a percentage; weights are summed.</param>
public sealed record LootEntry(string ItemKey, int Weight);

public sealed record MonsterDefinition(
    string Key,
    string Name,
    string Blurb,
    int Level,
    int ArmourClass,
    int MaxHitPoints,
    int AttackBonus,
    string DamageNotation,
    int MinGold,
    int MaxGold,
    /// <summary>Chance in 100 that a win drops anything at all.</summary>
    int DropChance,
    IReadOnlyList<LootEntry> LootTable)
{
    public DiceExpression Damage => DiceExpression.Parse(DamageNotation);

    /// <summary>
    /// Monsters within one level either way of the character, so the tavern always has
    /// something to fight without offering a certain death.
    /// </summary>
    public bool IsAvailableAt(int characterLevel) =>
        Level <= characterLevel + 1 && Level >= characterLevel - 2;
}

/// <summary>Code-held, following DEC-004. Only the key is ever persisted.</summary>
public static class MonsterCatalog
{
    public const string GiantRat = "giant-rat";
    public const string Goblin = "goblin";
    public const string Skeleton = "skeleton";
    public const string DireWolf = "dire-wolf";
    public const string Bandit = "bandit";
    public const string Ogre = "ogre";
    public const string Wraith = "wraith";
    public const string YoungDragon = "young-dragon";

    public static IReadOnlyList<MonsterDefinition> All { get; } =
    [
        new(GiantRat, "Giant Rat",
            "Startlingly large. Deeply unbothered by you.",
            Level: 1, ArmourClass: 10, MaxHitPoints: 7, AttackBonus: 2, DamageNotation: "1d4",
            MinGold: 1, MaxGold: 5, DropChance: 15,
            [
                new LootEntry(ItemCatalog.WornDagger, 3),
                new LootEntry(ItemCatalog.PaddedJerkin, 2),
                new LootEntry(ItemCatalog.LeatherArmour, 1),
                new LootEntry(ItemCatalog.LuckyCoin, 1)
            ]),

        new(Goblin, "Goblin",
            "Small, mean, and better armed than it has any right to be.",
            Level: 1, ArmourClass: 12, MaxHitPoints: 10, AttackBonus: 3, DamageNotation: "1d6",
            MinGold: 3, MaxGold: 12, DropChance: 30,
            [
                new LootEntry(ItemCatalog.GoblinCleaver, 4),
                new LootEntry(ItemCatalog.LeatherArmour, 3),
                new LootEntry(ItemCatalog.IronMace, 2),
                new LootEntry(ItemCatalog.GlovesOfTheThief, 2),
                new LootEntry(ItemCatalog.BootsOfSpeed, 1)
            ]),

        new(Skeleton, "Skeleton",
            "Rattles ominously. Does not appear to hold a grudge, exactly.",
            Level: 2, ArmourClass: 13, MaxHitPoints: 16, AttackBonus: 4, DamageNotation: "1d6+1",
            MinGold: 5, MaxGold: 18, DropChance: 35,
            [
                new LootEntry(ItemCatalog.RustyLongsword, 3),
                new LootEntry(ItemCatalog.ChainShirt, 3),
                new LootEntry(ItemCatalog.OakenStaff, 2),
                new LootEntry(ItemCatalog.StuddedLeather, 2),
                new LootEntry(ItemCatalog.RingOfFocus, 2),
                new LootEntry(ItemCatalog.AmuletOfInsight, 1)
            ]),

        new(Bandit, "Bandit",
            "Wants your gold. Has clearly done this before.",
            Level: 3, ArmourClass: 13, MaxHitPoints: 22, AttackBonus: 4, DamageNotation: "1d8",
            MinGold: 12, MaxGold: 35, DropChance: 40,
            [
                new LootEntry(ItemCatalog.SilveredBlade, 2),
                new LootEntry(ItemCatalog.DuellingRapier, 3),
                new LootEntry(ItemCatalog.StuddedLeather, 3),
                new LootEntry(ItemCatalog.ChoirmastersLute, 2),
                new LootEntry(ItemCatalog.CharmOfPresence, 2)
            ]),

        new(DireWolf, "Dire Wolf",
            "Quick, patient, and entirely too interested in your ankles.",
            Level: 4, ArmourClass: 14, MaxHitPoints: 30, AttackBonus: 5, DamageNotation: "2d4+1",
            MinGold: 8, MaxGold: 25, DropChance: 40,
            [
                new LootEntry(ItemCatalog.BootsOfSpeed, 4),
                new LootEntry(ItemCatalog.HuntingBow, 3),
                new LootEntry(ItemCatalog.PendantOfTheBear, 2),
                new LootEntry(ItemCatalog.ShadowweaveCloak, 1),
                new LootEntry(ItemCatalog.RingOfVigour, 1)
            ]),

        new(Ogre, "Ogre",
            "Slow to anger, slower to stop. Smells of wet rope.",
            Level: 6, ArmourClass: 15, MaxHitPoints: 48, AttackBonus: 6, DamageNotation: "2d6+2",
            MinGold: 25, MaxGold: 70, DropChance: 50,
            [
                new LootEntry(ItemCatalog.ScaleMail, 4),
                new LootEntry(ItemCatalog.GreatAxe, 3),
                new LootEntry(ItemCatalog.GoblinCleaver, 3),
                new LootEntry(ItemCatalog.PendantOfTheBear, 2),
                new LootEntry(ItemCatalog.RingOfVigour, 2)
            ]),

        new(Wraith, "Wraith",
            "Cold where it passes. Does not seem to notice walls.",
            Level: 8, ArmourClass: 17, MaxHitPoints: 60, AttackBonus: 7, DamageNotation: "2d8",
            MinGold: 40, MaxGold: 110, DropChance: 55,
            [
                new LootEntry(ItemCatalog.SilveredBlade, 4),
                new LootEntry(ItemCatalog.RunedWand, 3),
                new LootEntry(ItemCatalog.ShadowweaveCloak, 2),
                new LootEntry(ItemCatalog.WardingShield, 2),
                new LootEntry(ItemCatalog.CircletOfClarity, 3),
                new LootEntry(ItemCatalog.AmuletOfInsight, 3)
            ]),

        new(YoungDragon, "Young Dragon",
            "Barely more than a hatchling. Still a dragon.",
            Level: 11, ArmourClass: 18, MaxHitPoints: 95, AttackBonus: 9, DamageNotation: "2d10+3",
            MinGold: 120, MaxGold: 300, DropChance: 75,
            [
                new LootEntry(ItemCatalog.DragonfangSpear, 3),
                new LootEntry(ItemCatalog.LongbowOfTheVale, 3),
                new LootEntry(ItemCatalog.ReliquaryHammer, 2),
                new LootEntry(ItemCatalog.BreastplateOfDawn, 2),
                new LootEntry(ItemCatalog.WardingShield, 3),
                new LootEntry(ItemCatalog.ScaleMail, 2)
            ])
    ];

    private static readonly Dictionary<string, MonsterDefinition> ByKey =
        All.ToDictionary(m => m.Key, StringComparer.Ordinal);

    public static MonsterDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);

    public static IReadOnlyList<MonsterDefinition> AvailableAt(int characterLevel) =>
        All.Where(m => m.IsAvailableAt(characterLevel)).OrderBy(m => m.Level).ToList();
}
