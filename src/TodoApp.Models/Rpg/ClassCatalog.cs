using TodoApp.Models.Dice;

namespace TodoApp.Models.Rpg;

/// <summary>A passive class ability. Each one bends exactly one rule.</summary>
public enum ClassPerk
{
    /// <summary>Fighter: recover hit points on a win.</summary>
    SecondWind = 0,

    /// <summary>Rogue: critical hits land on a natural 19 as well as 20.</summary>
    SneakAttack = 1,

    /// <summary>Wizard: encounters sometimes cost no stamina.</summary>
    ArcaneRecovery = 2,

    /// <summary>Cleric: the first natural 1 in an encounter is rerolled.</summary>
    Blessing = 3,

    /// <summary>Ranger: loot rarity rolls are made with advantage.</summary>
    FavouredQuarry = 4,

    /// <summary>Bard: gold rewards are increased by half.</summary>
    SilverTongue = 5
}

public sealed record CharacterClass(
    string Key,
    string Name,
    string Blurb,
    int HitDieSides,
    Ability Primary,
    Ability Secondary,
    AbilityScores StartingScores,
    ClassPerk Perk,
    string PerkName,
    string PerkDescription,
    string StartingWeaponKey,
    string StartingArmourKey)
{
    public DiceExpression HitDie => new(1, HitDieSides, 0);
}

/// <summary>
/// Code-held, following DEC-004: adding a class ships without a migration, because only
/// the chosen key is persisted.
/// </summary>
public static class ClassCatalog
{
    public const string Fighter = "fighter";
    public const string Rogue = "rogue";
    public const string Wizard = "wizard";
    public const string Cleric = "cleric";
    public const string Ranger = "ranger";
    public const string Bard = "bard";

    public static IReadOnlyList<CharacterClass> All { get; } =
    [
        new(Fighter, "Fighter",
            "Straightforward and hard to put down. Hits things until they stop moving.",
            HitDieSides: 10, Ability.Strength, Ability.Constitution,
            new AbilityScores(Strength: 16, Dexterity: 12, Constitution: 15, Intelligence: 8, Wisdom: 10, Charisma: 11),
            ClassPerk.SecondWind, "Second Wind",
            "Recover a quarter of your hit points whenever you win a fight.",
            ItemCatalog.RustyLongsword, ItemCatalog.ChainShirt),

        new(Rogue, "Rogue",
            "Precise and opportunistic. Finds the gap in the armour rather than the armour.",
            HitDieSides: 8, Ability.Dexterity, Ability.Intelligence,
            new AbilityScores(Strength: 10, Dexterity: 16, Constitution: 12, Intelligence: 14, Wisdom: 11, Charisma: 13),
            ClassPerk.SneakAttack, "Sneak Attack",
            "Your critical hits land on a natural 19 as well as a 20.",
            ItemCatalog.WornDagger, ItemCatalog.LeatherArmour),

        new(Wizard, "Wizard",
            "Thinks first. Conserves energy that others would burn.",
            HitDieSides: 6, Ability.Intelligence, Ability.Wisdom,
            new AbilityScores(Strength: 8, Dexterity: 13, Constitution: 12, Intelligence: 16, Wisdom: 14, Charisma: 10),
            ClassPerk.ArcaneRecovery, "Arcane Recovery",
            "Roughly one fight in four costs you no stamina at all.",
            ItemCatalog.CrackedQuarterstaff, ItemCatalog.TravellersRobes),

        new(Cleric, "Cleric",
            "Steady and lucky in the way that looks like providence.",
            HitDieSides: 8, Ability.Wisdom, Ability.Constitution,
            new AbilityScores(Strength: 13, Dexterity: 10, Constitution: 14, Intelligence: 11, Wisdom: 16, Charisma: 12),
            ClassPerk.Blessing, "Blessing",
            "The first natural 1 you roll in a fight is rerolled.",
            ItemCatalog.PlainMace, ItemCatalog.ChainShirt),

        new(Ranger, "Ranger",
            "Knows where the good things are kept.",
            HitDieSides: 10, Ability.Dexterity, Ability.Wisdom,
            new AbilityScores(Strength: 12, Dexterity: 16, Constitution: 13, Intelligence: 10, Wisdom: 14, Charisma: 10),
            ClassPerk.FavouredQuarry, "Favoured Quarry",
            "Roll loot rarity with advantage, so rare drops come more often.",
            ItemCatalog.HuntingBow, ItemCatalog.LeatherArmour),

        new(Bard, "Bard",
            "Gets paid more for the same work, somehow.",
            HitDieSides: 8, Ability.Charisma, Ability.Dexterity,
            new AbilityScores(Strength: 10, Dexterity: 14, Constitution: 12, Intelligence: 12, Wisdom: 11, Charisma: 16),
            ClassPerk.SilverTongue, "Silver Tongue",
            "Every gold reward is increased by half.",
            ItemCatalog.WornDagger, ItemCatalog.TravellersRobes)
    ];

    private static readonly Dictionary<string, CharacterClass> ByKey =
        All.ToDictionary(c => c.Key, StringComparer.Ordinal);

    public static CharacterClass? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);
}
