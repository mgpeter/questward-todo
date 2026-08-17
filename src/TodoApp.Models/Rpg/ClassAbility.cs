namespace TodoApp.Models.Rpg;

/// <summary>
/// How an ability resolves. <see cref="CombatService"/> switches on this, so each kind is
/// a distinct, individually testable branch rather than a bag of flags.
/// </summary>
public enum AbilityKind
{
    /// <summary>Fighter: harder to land, but the damage dice are doubled.</summary>
    PowerAttack = 0,

    /// <summary>Rogue: attack with advantage.</summary>
    SneakStrike = 1,

    /// <summary>Wizard: no attack roll at all, always connects.</summary>
    MagicMissile = 2,

    /// <summary>Cleric: heals instead of attacking.</summary>
    HealingWord = 3,

    /// <summary>Ranger: advantage, and criticals land on a 19.</summary>
    AimedShot = 4,

    /// <summary>Bard: light damage, and the monster's counter-attack is at disadvantage.</summary>
    ViciousMockery = 5
}

/// <param name="UsesPerEncounter">
/// Reset every fight. Deliberately not a persistent pool: a todo app should not ask
/// someone to manage spell slots across days.
/// </param>
public sealed record ClassAbility(
    string Key,
    string Name,
    string Description,
    AbilityKind Kind,
    int UsesPerEncounter);

public static class ClassAbilities
{
    public const string PowerAttack = "power-attack";
    public const string SneakStrike = "sneak-strike";
    public const string MagicMissile = "magic-missile";
    public const string HealingWord = "healing-word";
    public const string AimedShot = "aimed-shot";
    public const string ViciousMockery = "vicious-mockery";

    /// <summary>Attack penalty for a Power Attack, traded for doubled damage dice.</summary>
    public const int PowerAttackPenalty = -2;

    /// <summary>Rounds of disadvantage Vicious Mockery inflicts.</summary>
    public const int MockeryRounds = 1;

    public static IReadOnlyList<ClassAbility> For(string? classKey) => classKey switch
    {
        ClassCatalog.Fighter =>
        [
            new(PowerAttack, "Power Attack",
                "Swing hard: -2 to hit, but the damage dice are doubled.",
                AbilityKind.PowerAttack, 2)
        ],
        ClassCatalog.Rogue =>
        [
            new(SneakStrike, "Sneak Strike",
                "Find the gap: attack with advantage.",
                AbilityKind.SneakStrike, 2)
        ],
        ClassCatalog.Wizard =>
        [
            new(MagicMissile, "Magic Missile",
                "Unerring force. No attack roll, always hits for 3d4 plus your Intelligence.",
                AbilityKind.MagicMissile, 2)
        ],
        ClassCatalog.Cleric =>
        [
            new(HealingWord, "Healing Word",
                "Mend yourself for 1d8 plus your Wisdom. You do not attack this round.",
                AbilityKind.HealingWord, 2)
        ],
        ClassCatalog.Ranger =>
        [
            new(AimedShot, "Aimed Shot",
                "Take your time: advantage, and criticals land on a 19.",
                AbilityKind.AimedShot, 2)
        ],
        ClassCatalog.Bard =>
        [
            new(ViciousMockery, "Vicious Mockery",
                "A cutting remark: light damage, and its answering swing goes wide.",
                AbilityKind.ViciousMockery, 2)
        ],
        _ => []
    };

    public static ClassAbility? Find(string? classKey, string? abilityKey) =>
        abilityKey is null
            ? null
            : For(classKey).FirstOrDefault(a => a.Key == abilityKey);
}
