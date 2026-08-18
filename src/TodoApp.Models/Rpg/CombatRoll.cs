using TodoApp.Models.Dice;

namespace TodoApp.Models.Rpg;

/// <summary>
/// One entry in the combat log, shaped for both persistence and the wire.
/// </summary>
/// <remarks>
/// Carries every die and every labelled modifier rather than just the total. The client
/// renders the arithmetic, which is what makes a loss read as bad luck rather than an
/// unexplained verdict.
/// </remarks>
/// <param name="Text">
/// The whole line, mechanical clause and flavour together. Complete on its own, so a screen
/// reader and a log written before <see cref="Flavour"/> existed both still read properly.
/// </param>
/// <param name="Flavour">
/// The narrative tail of <paramref name="Text"/>, or null when the line is purely mechanical.
/// </param>
public sealed record CombatRoll(
    int Round,
    string Actor,
    string Kind,
    IReadOnlyList<DieRoll> Dice,
    IReadOnlyList<RollModifier> Modifiers,
    int Total,
    int? Target,
    string Outcome,
    bool Critical,
    string Text,
    string? Flavour = null)
{
    public const string Player = "player";
    public const string Monster = "monster";

    public static CombatRoll From(
        int round,
        string actor,
        RollResult result,
        string text,
        string? flavour = null) =>
        new(
            round,
            actor,
            result.Kind.ToString().ToLowerInvariant(),
            result.Dice,
            result.Modifiers,
            result.Total,
            result.Target,
            result.Outcome switch
            {
                RollOutcome.Hit => result.Critical ? "critical" : "hit",
                RollOutcome.Miss => result.CriticalFailure ? "fumble" : "miss",
                _ => "none"
            },
            result.Critical,
            text,
            flavour);

    /// <summary>A narrative line, for the log and for screen readers.</summary>
    public static CombatRoll Note(int round, string actor, string text, string? flavour = null) =>
        new(round, actor, "note", [], [], 0, null, "none", false, text, flavour);

    /// <summary>
    /// A mechanical clause with a flavour line appended, marked so the client does not have to
    /// guess where the seam is.
    /// </summary>
    /// <remarks>
    /// The client used to find the seam by cutting at the last sentence break, which is wrong
    /// for every mechanical line that is already two sentences: "6 damage. Goblin has 4 hit
    /// points left." rendered the remaining hit points, the one number the player is tracking,
    /// in the faint decorative style reserved for narration. Only this side knows whether a
    /// flavour line was appended, so this side is where it is recorded.
    /// </remarks>
    public static string Compose(string clause, string flavour) => $"{clause} {flavour}";
}
