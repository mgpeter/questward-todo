namespace TodoApp.Models.Rpg;

public enum Ability
{
    Strength = 0,
    Dexterity = 1,
    Constitution = 2,
    Intelligence = 3,
    Wisdom = 4,
    Charisma = 5
}

/// <summary>
/// The six ability scores. A score of 10 is average and gives no modifier.
/// </summary>
public readonly record struct AbilityScores(
    int Strength = 10,
    int Dexterity = 10,
    int Constitution = 10,
    int Intelligence = 10,
    int Wisdom = 10,
    int Charisma = 10)
{
    /// <summary>An unremarkable human: 10 across the board, every modifier +0.</summary>
    /// <remarks>
    /// Written out in full on purpose. <c>new AbilityScores()</c> would invoke the struct's
    /// implicit parameterless constructor, which zero-initialises and ignores the primary
    /// constructor's defaults, producing a character with -5 to everything.
    /// </remarks>
    public static AbilityScores Default { get; } = new(10, 10, 10, 10, 10, 10);

    /// <summary>All zeroes: the additive identity, used for item bonuses.</summary>
    public static AbilityScores Zero { get; } = new(0, 0, 0, 0, 0, 0);

    public int this[Ability ability] => ability switch
    {
        Ability.Strength => Strength,
        Ability.Dexterity => Dexterity,
        Ability.Constitution => Constitution,
        Ability.Intelligence => Intelligence,
        Ability.Wisdom => Wisdom,
        Ability.Charisma => Charisma,
        _ => 10
    };

    /// <summary>
    /// The tabletop modifier: <c>floor((score - 10) / 2)</c>.
    /// </summary>
    /// <remarks>
    /// Must floor rather than truncate. C# integer division rounds toward zero, so a score
    /// of 7 would give -1 instead of the correct -2, quietly making weak characters
    /// stronger than the rules allow.
    /// </remarks>
    public static int ModifierFor(int score) => (int)Math.Floor((score - 10) / 2.0);

    public int Modifier(Ability ability) => ModifierFor(this[ability]);

    public AbilityScores With(Ability ability, int score) => ability switch
    {
        Ability.Strength => this with { Strength = score },
        Ability.Dexterity => this with { Dexterity = score },
        Ability.Constitution => this with { Constitution = score },
        Ability.Intelligence => this with { Intelligence = score },
        Ability.Wisdom => this with { Wisdom = score },
        Ability.Charisma => this with { Charisma = score },
        _ => this
    };

    public AbilityScores Plus(Ability ability, int delta) => With(ability, this[ability] + delta);

    public AbilityScores Plus(AbilityScores other) => new(
        Strength + other.Strength,
        Dexterity + other.Dexterity,
        Constitution + other.Constitution,
        Intelligence + other.Intelligence,
        Wisdom + other.Wisdom,
        Charisma + other.Charisma);

    /// <summary>All six, in the conventional tabletop order.</summary>
    public static IReadOnlyList<Ability> All { get; } = Enum.GetValues<Ability>();

    public static string Abbreviate(Ability ability) => ability switch
    {
        Ability.Strength => "STR",
        Ability.Dexterity => "DEX",
        Ability.Constitution => "CON",
        Ability.Intelligence => "INT",
        Ability.Wisdom => "WIS",
        Ability.Charisma => "CHA",
        _ => "???"
    };
}
