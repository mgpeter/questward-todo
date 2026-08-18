using System.Text.Json;

namespace TodoApp.Models.Rpg;

/// <summary>Who an effect sits on. Both sides share one array, because both die with the fight.</summary>
public enum EffectTarget
{
    Player = 0,
    Monster = 1
}

/// <summary>
/// What an effect does. One member per place in the round it is read, so a new effect is a
/// new case rather than another flag threaded through the attack path.
/// </summary>
public enum EffectKind
{
    /// <summary>The target's attack rolls are made at disadvantage. Magnitude is unused.</summary>
    Weakened = 0,

    /// <summary>Magnitude is added to the target's attack roll and to the damage that attack deals.</summary>
    Empowered = 1,

    /// <summary>Magnitude is added to the armour class an attack on the target is measured against.</summary>
    Guarded = 2,

    /// <summary>The target loses Magnitude hit points at the end of the round.</summary>
    Poisoned = 3,

    /// <summary>The target regains Magnitude hit points at the end of the round.</summary>
    Regenerating = 4
}

/// <summary>
/// One affliction or blessing riding a fight.
/// </summary>
/// <remarks>
/// Lives on <see cref="Encounter"/> rather than <see cref="Character"/>, so nothing has to
/// clean up after a fight and nothing can leak into the next one. The fight ends, the row
/// stops being read, and the effect is gone with it.
/// </remarks>
/// <param name="Rounds">
/// Applications remaining, not rounds elapsed. An effect is spent by being applied, at the one
/// site that applies it, which is why the Bard's remark is still consumed by the counter-attack
/// in its own round rather than lingering into the next one.
/// </param>
/// <param name="Magnitude">
/// Fixed when the effect is applied and never rolled. A tick that drew from IDiceRoller would
/// shift every hard-coded SequenceDiceRoller script in the suite at once.
/// </param>
/// <param name="Source">Key of whatever applied it: an ability, an item or a monster phase.</param>
public sealed record StatusEffect(
    EffectKind Kind,
    EffectTarget Target,
    int Rounds,
    int Magnitude,
    string Source);

/// <summary>
/// The whole lifecycle of a status effect: read it, apply it, spend it, prune it, write it.
/// </summary>
/// <remarks>
/// Pure and static on purpose. Nothing here takes an <c>IDiceRoller</c> and nothing here takes
/// a DbContext, so the rules can be asserted without a database and, more importantly, cannot
/// quietly acquire a die. Every SequenceDiceRoller script in the test suite hard-codes how many
/// rolls a round consumes and in what order; a magnitude rolled at tick time would shift all of
/// them at once and dozens of tests would keep passing while asserting something else.
/// </remarks>
public static class StatusEffects
{
    /// <summary>
    /// Rounds for an effect meant to last the whole fight rather than a stated number of
    /// applications. Large rather than infinite so the same spend-on-use path still applies
    /// and there is no second lifecycle to reason about.
    /// </summary>
    public const int Lasting = 99;

    /// <summary>
    /// The order the end-of-round tick fires in: harm before healing, so a magnitude that
    /// exactly cancels another reads the same way every time.
    /// </summary>
    private static readonly EffectKind[] TickOrder = [EffectKind.Poisoned, EffectKind.Regenerating];

    /// <summary>Reads the effects riding an encounter.</summary>
    /// <remarks>
    /// A corrupt blob clears the afflictions rather than bricking a live fight, copying
    /// <c>ReadUses</c>. The trade is deliberate and one-sided: the cost of swallowing is one
    /// fight losing its status effects, and the cost of throwing is that fight becoming
    /// unplayable with no way for the player to get out of it.
    /// </remarks>
    public static List<StatusEffect> Read(Encounter encounter)
    {
        if (string.IsNullOrWhiteSpace(encounter.Effects))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<StatusEffect>>(encounter.Effects) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Serialises the effects back onto the encounter, spent entries dropped.</summary>
    /// <remarks>
    /// Pruning here rather than at the call site is what guarantees a spent effect never
    /// reaches the database or the wire, whichever path spent it.
    /// <para>
    /// Serialised with no options on purpose, matching the log and the ability uses beside it:
    /// property names are PascalCase and enums are numbers, which is exactly the shape the
    /// AddStatusEffects migration's backfill writes. Options here and a literal there would
    /// not throw, they would bind defaults and silently produce a Weakened of zero rounds.
    /// </para>
    /// </remarks>
    public static void Write(Encounter encounter, IReadOnlyList<StatusEffect> effects) =>
        encounter.Effects = JsonSerializer.Serialize(Prune(effects));

    /// <summary>Drops entries with nothing left in them.</summary>
    public static IReadOnlyList<StatusEffect> Prune(IReadOnlyList<StatusEffect> effects) =>
        [.. effects.Where(e => e.Rounds > 0)];

    /// <summary>
    /// Puts an effect on the board, refreshing rather than stacking.
    /// </summary>
    /// <remarks>
    /// At most one entry per kind and target, and the incoming one wins only when it lasts at
    /// least as long, so a weaker reapplication cannot cut a stronger effect short. This is the
    /// assignment-not-accumulation rule Vicious Mockery already had, generalised: chugging five
    /// poisons for five times the damage is combat-power inflation of the family DEC-003 exists
    /// to refuse.
    /// </remarks>
    public static void Apply(List<StatusEffect> effects, StatusEffect incoming)
    {
        var index = effects.FindIndex(e => e.Kind == incoming.Kind && e.Target == incoming.Target);

        if (index < 0)
        {
            effects.Add(incoming);
            return;
        }

        if (incoming.Rounds >= effects[index].Rounds)
        {
            effects[index] = incoming;
        }
    }

    /// <summary>The effect of this kind in force on this target, or null when there is none.</summary>
    /// <remarks>
    /// An entry spent down to nothing is not in force. Reporting one would let a caller read a
    /// magnitude out of an effect that has already done its work, in the window between the
    /// spend and the prune.
    /// </remarks>
    public static StatusEffect? Find(
        IReadOnlyList<StatusEffect> effects,
        EffectKind kind,
        EffectTarget target) =>
        effects.FirstOrDefault(e => e.Kind == kind && e.Target == target && e.Rounds > 0);

    /// <summary>The magnitude in force, or zero. The shape every arithmetic site wants.</summary>
    public static int MagnitudeOf(
        IReadOnlyList<StatusEffect> effects,
        EffectKind kind,
        EffectTarget target) =>
        Find(effects, kind, target)?.Magnitude ?? 0;

    /// <summary>
    /// Consumes one application. Does nothing when there is none, so every read site can spend
    /// unconditionally rather than testing first.
    /// </summary>
    public static void Spend(List<StatusEffect> effects, EffectKind kind, EffectTarget target)
    {
        var index = effects.FindIndex(e => e.Kind == kind && e.Target == target && e.Rounds > 0);

        if (index >= 0)
        {
            effects[index] = effects[index] with { Rounds = effects[index].Rounds - 1 };
        }
    }

    /// <summary>
    /// The end-of-round ticks in force, in the order they fire, each spent as it is read.
    /// </summary>
    /// <remarks>
    /// Returns the entries as they stood before spending, so the caller has the magnitude and
    /// the source to narrate. Takes no roller and can therefore never take a die, which is the
    /// property the whole placement of the tick was chosen to preserve.
    /// </remarks>
    public static IReadOnlyList<StatusEffect> Tick(List<StatusEffect> effects)
    {
        List<StatusEffect> firing = [];

        foreach (var kind in TickOrder)
        {
            // Array order within a kind, so a reloaded fight ticks the way the first one did.
            firing.AddRange(effects.Where(e => e.Kind == kind && e.Rounds > 0));
        }

        foreach (var effect in firing)
        {
            Spend(effects, effect.Kind, effect.Target);
        }

        return firing;
    }
}
