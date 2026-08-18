namespace TodoApp.Models.Rpg;

/// <summary>
/// One rung of the hunt ladder: what a fight of this level is worth being, in the abstract.
/// </summary>
/// <remarks>
/// Lifted from the bestiary's own curve rather than invented, so a hunt is never absurd beside
/// a catalog monster of the same level. A separate curve would drift from the tavern's the
/// first time either was tuned, and the player would find out by losing.
/// </remarks>
public sealed record HuntRung(
    int Level,
    int ArmourClass,
    int HitPoints,
    int AttackBonus,
    string DamageNotation,
    int MinGold,
    int MaxGold,
    int DropChance);

/// <summary>Code-held per DEC-004. Nothing here is persisted; the level alone is.</summary>
public static class HuntLadder
{
    /// <summary>
    /// One row per level, in order, taken from the first monster the bestiary declares at that
    /// level.
    /// </summary>
    /// <remarks>
    /// Indexed by level rather than searched, so row zero is level one and row thirteen is level
    /// fourteen. <see cref="At"/> depends on that and a catalog guard is the place to hold it.
    /// <para>
    /// The gold at level four is deliberately left lower than at level three, because the
    /// bestiary's own curve dips there and this ladder is lifted rather than smoothed. Smoothing
    /// it here would make a hunt richer than the monster it was priced against, which is the one
    /// property the lift exists to guarantee.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<HuntRung> All { get; } =
    [
        new(Level: 1, ArmourClass: 10, HitPoints: 7, AttackBonus: 2,
            DamageNotation: "1d4", MinGold: 1, MaxGold: 5, DropChance: 15),
        new(Level: 2, ArmourClass: 13, HitPoints: 16, AttackBonus: 4,
            DamageNotation: "1d6+1", MinGold: 5, MaxGold: 18, DropChance: 35),
        new(Level: 3, ArmourClass: 13, HitPoints: 22, AttackBonus: 4,
            DamageNotation: "1d8", MinGold: 12, MaxGold: 35, DropChance: 40),
        new(Level: 4, ArmourClass: 14, HitPoints: 30, AttackBonus: 5,
            DamageNotation: "2d4+1", MinGold: 8, MaxGold: 25, DropChance: 40),
        new(Level: 5, ArmourClass: 15, HitPoints: 38, AttackBonus: 6,
            DamageNotation: "1d12", MinGold: 20, MaxGold: 50, DropChance: 45),
        new(Level: 6, ArmourClass: 15, HitPoints: 48, AttackBonus: 6,
            DamageNotation: "2d6+2", MinGold: 25, MaxGold: 70, DropChance: 50),
        new(Level: 7, ArmourClass: 16, HitPoints: 54, AttackBonus: 7,
            DamageNotation: "2d6+3", MinGold: 35, MaxGold: 90, DropChance: 52),
        new(Level: 8, ArmourClass: 17, HitPoints: 60, AttackBonus: 7,
            DamageNotation: "2d8", MinGold: 40, MaxGold: 110, DropChance: 55),
        new(Level: 9, ArmourClass: 17, HitPoints: 70, AttackBonus: 8,
            DamageNotation: "2d8+2", MinGold: 60, MaxGold: 150, DropChance: 60),
        new(Level: 10, ArmourClass: 18, HitPoints: 82, AttackBonus: 8,
            DamageNotation: "3d6", MinGold: 90, MaxGold: 210, DropChance: 65),
        new(Level: 11, ArmourClass: 18, HitPoints: 95, AttackBonus: 9,
            DamageNotation: "2d10+3", MinGold: 120, MaxGold: 300, DropChance: 75),
        new(Level: 12, ArmourClass: 19, HitPoints: 105, AttackBonus: 10,
            DamageNotation: "2d10+4", MinGold: 150, MaxGold: 340, DropChance: 78),
        new(Level: 13, ArmourClass: 19, HitPoints: 118, AttackBonus: 10,
            DamageNotation: "3d8+2", MinGold: 180, MaxGold: 400, DropChance: 80),
        new(Level: 14, ArmourClass: 20, HitPoints: 132, AttackBonus: 11,
            DamageNotation: "2d12+4", MinGold: 220, MaxGold: 480, DropChance: 85)
    ];

    /// <summary>
    /// The rung a hunt of this level is built on.
    /// </summary>
    /// <remarks>
    /// Clamped rather than throwing. A stored HuntLevel is a historical fact about a fight that
    /// is already open, so a ladder shortened by a later tune must still resolve every row
    /// already out there: the alternative is a live encounter that throws on every read and can
    /// never be finished or fled.
    /// </remarks>
    public static HuntRung At(int level) => All[Math.Clamp(level, 1, All.Count) - 1];
}

/// <summary>
/// The whole derivation of a hunt, from the four facts frozen on the encounter row to the stat
/// block the one combat loop reads.
/// </summary>
/// <remarks>
/// Pure and static on purpose, and in the one sense that matters: nothing here takes an
/// <c>IDiceRoller</c>, so a stat block can never quietly acquire a die. Every SequenceDiceRoller
/// script in the suite hard-codes the order a round consumes its dice, and a roll spent while a
/// block was being computed would land before the attack roll and change what dozens of passing
/// tests assert without failing any of them.
/// <para>
/// Nothing here is stored either. The inputs are rolled or historical facts and live on the row
/// (DEC-002); everything below is recomputed on every read, which is what lets a tuning change
/// reach fights already in progress the way a boss retune already does (DEC-004).
/// </para>
/// </remarks>
public static class HuntRules
{
    /// <summary>
    /// Subtasks past this many add no further bulk.
    /// </summary>
    /// <remarks>
    /// A checklist of forty items is a planning style, not a forty-times-larger monster.
    /// </remarks>
    public const int CountedSubtaskCap = 10;

    /// <summary>
    /// The ceiling on a hunt's hit points, as a multiple of the rung it is built on.
    /// </summary>
    /// <remarks>
    /// A hunt costs one stamina however long it runs, so nothing about a huge health pool makes
    /// it harder; it only makes it longer. An uncapped Dread with ten subtasks is a twenty-round
    /// chore, and a game whose joke is that doing the actual task would have been faster should
    /// not be making that joke by accident.
    /// </remarks>
    public const int HitPointsCapMultiple = 2;

    /// <summary>Nothing is ever a certain drop, however overdue or however decorated.</summary>
    public const int DropChanceCap = 95;

    /// <summary>The most of a task title the opening line will quote.</summary>
    private const int TitleLimit = 60;

    /// <summary>
    /// The rung a contract is written at: difficulty against the hunter's band, never age.
    /// </summary>
    /// <remarks>
    /// The offsets are exactly the band <see cref="MonsterDefinition.IsAvailableAt"/> already
    /// enforces (Level &lt;= level + 1 and Level &gt;= level - 2), so a hunt is never outside the
    /// range the tavern would offer anyway. That is one assertion rather than a second balance
    /// curve to keep in step.
    /// <para>
    /// Age is deliberately absent. If waiting promoted the rung, stalling an Easy task would pay
    /// an Epic's gold, and the backlog would become the optimal way to earn rather than the
    /// interesting thing to clear (DEC-013). The 30 day promotion moves the shape and nothing
    /// else.
    /// </para>
    /// </remarks>
    public static int LevelFor(int characterLevel, Difficulty difficulty) =>
        Math.Clamp(
            MonsterCatalog.BandLevel(characterLevel) + Step(difficulty),
            1,
            MonsterCatalog.TopLevel);

    private static int Step(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => -2,
        Difficulty.Medium => -1,
        Difficulty.Hard => 0,
        Difficulty.Epic => 1,
        _ => -1
    };

    /// <summary>
    /// The monster's name: an epithet earned by age, and the archetype's noun. Both catalog.
    /// </summary>
    /// <remarks>
    /// No article, because the flavour lines supply their own ("The {monster} sees you and stays
    /// exactly where it is"), and no task title, because this string ends up in the combat log,
    /// the chronicle and EncounterDto.MonsterName. <see cref="OpeningLine"/> is the one place
    /// the user's own words appear.
    /// </remarks>
    public static string NameFor(HuntArchetype archetype, int daysOverdue) =>
        HuntArchetypeCatalog.Epithet(daysOverdue) is { } epithet
            ? $"{epithet} {archetype.Noun}"
            : archetype.Noun;

    /// <summary>
    /// The stat block for a hunt, derived from the four facts the encounter row froze.
    /// </summary>
    /// <remarks>
    /// Stable across reads because every input is on the row, which is the property three call
    /// sites depend on and none of them can state for themselves:
    /// <see cref="MonsterDefinition.PhaseAt"/> cross-multiplies against MaxHitPoints and would
    /// re-fire or skip a phase if the denominator moved, TickMonster caps regeneration at it,
    /// and EncounterDto.MonsterMaxHitPoints is the client's health bar.
    /// <para>
    /// All integer, and multiplied before divided everywhere, for the reason <c>PhaseAt</c>
    /// gives: dividing into a percentage first rounds one end of the ladder away entirely, and a
    /// 7 hit point rung has no room for that.
    /// </para>
    /// </remarks>
    /// <param name="level">The rung the contract was written at, frozen at start.</param>
    /// <param name="daysOverdue">Days overdue when the contract was taken, frozen at start.</param>
    /// <param name="subtaskCount">Subtasks when the contract was taken, frozen at start.</param>
    public static MonsterDefinition StatBlock(
        HuntArchetype archetype,
        int level,
        int daysOverdue,
        int subtaskCount)
    {
        var rung = HuntLadder.At(level);
        var bounty = BountyRules.BountyPercent(daysOverdue);
        var counted = Math.Clamp(subtaskCount, 0, CountedSubtaskCap);

        var hitPoints = Math.Max(
            1,
            Math.Min(
                rung.HitPoints * HitPointsCapMultiple,
                (rung.HitPoints * archetype.HitPointsPercent / 100)
                    + (counted * archetype.HitPointsPerSubtask)));

        return new MonsterDefinition(
            archetype.Key,
            NameFor(archetype, daysOverdue),
            archetype.Blurb,
            Level: rung.Level,
            ArmourClass: rung.ArmourClass + archetype.ArmourClassStep,
            MaxHitPoints: hitPoints,
            AttackBonus: rung.AttackBonus + archetype.AttackBonusStep,
            DamageNotation: rung.DamageNotation,

            // The bounty is baked into the range rather than applied to the roll, which is what
            // leaves LootService untouched: RollGold draws one die from a wider span instead of
            // an extra die, so no seeded script shifts, the Bard's half is applied after the
            // roll by construction, and encounter.GoldAwarded still means what this contract was
            // worth. The multiplier is never below 100 (DEC-013).
            MinGold: rung.MinGold * bounty / 100,
            MaxGold: rung.MaxGold * bounty / 100,

            DropChance: Math.Min(DropChanceCap, rung.DropChance + archetype.DropChanceStep),
            archetype.LootTable,
            archetype.Phases);
    }

    /// <summary>
    /// The line that opens a hunt's log, and the only place a task's own words are used.
    /// </summary>
    /// <remarks>
    /// Composed once, at start, and history from then on. Keeping the title here and out of
    /// <see cref="MonsterDefinition.Name"/> is what leaves the rest of the combat log, the
    /// chronicle, the codex and EncounterDto.MonsterName free of user text, and what keeps the
    /// stored key a catalog key inside its varchar(60).
    /// </remarks>
    public static string OpeningLine(string monsterName, string taskTitle)
    {
        var subject = taskTitle.Trim();

        // A blank title would otherwise compose a line reading: rises from "."
        if (subject.Length == 0)
        {
            return $"The {monsterName} rises.";
        }

        // Titles are varchar(200) and the log is read as prose. One of them quoted whole is the
        // whole entry.
        if (subject.Length > TitleLimit)
        {
            subject = subject[..TitleLimit].TrimEnd() + "...";
        }

        // The stop sits inside the quotation, and only when the title did not bring its own.
        var stop = ".!?".Contains(subject[^1]) ? string.Empty : ".";

        return $"The {monsterName} rises from \"{subject}{stop}\"";
    }
}
