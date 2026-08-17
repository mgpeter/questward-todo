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
    string Text)
{
    public const string Player = "player";
    public const string Monster = "monster";

    public static CombatRoll From(int round, string actor, RollResult result, string text) =>
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
            text);

    /// <summary>A narrative line, for the log and for screen readers.</summary>
    public static CombatRoll Note(int round, string actor, string text) =>
        new(round, actor, "note", [], [], 0, null, "none", false, text);
}
