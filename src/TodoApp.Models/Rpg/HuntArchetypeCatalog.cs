namespace TodoApp.Models.Rpg;

/// <summary>
/// The shape a task takes when it is written up as a contract.
/// </summary>
/// <remarks>
/// Code-held per DEC-004: only the key is ever persisted, and it is persisted in the
/// encounter's existing MonsterKey column rather than a column of its own. Retuning an
/// archetype therefore reaches hunts already in progress, exactly as retuning a boss does.
/// <para>
/// Every key here is prefixed "hunt-", and the key space is disjoint from
/// <see cref="MonsterCatalog"/>'s because both land in that one varchar(60). A collision would
/// make a single stored key resolve to two different stat blocks depending on which catalog was
/// asked first, and the fight would change shape on the read that asked the other one.
/// </para>
/// </remarks>
/// <param name="Noun">
/// The second half of the monster's name, and the fixed half. The article is deliberately not
/// part of it: <see cref="FlavourCatalog"/> lines already read "The {monster} ...", so a noun
/// carrying its own "The" would render as "The The Bulwark".
/// </param>
/// <param name="HitPointsPercent">
/// Percent of the rung's hit points, applied before the per-subtask bulk is added.
/// </param>
/// <param name="HitPointsPerSubtask">
/// Bulk added per subtask, counted only up to <see cref="HuntRules.CountedSubtaskCap"/>.
/// </param>
/// <param name="ArmourClassStep">Added to the rung's armour class.</param>
/// <param name="AttackBonusStep">Added to the rung's attack bonus.</param>
/// <param name="DropChanceStep">Added to the rung's drop chance, then capped.</param>
/// <param name="LootTable">
/// Its own table, never an existing monster's. LootService.PickWeighted rolls once against a
/// table's summed weight and walks it in declaration order, so reweighting or extending a table
/// a seeded test can already reach would hand that test a different item, with no change in
/// roll count to make the break visible.
/// </param>
/// <param name="Phases">
/// Fixed magnitudes, never rolled, for the same reason <see cref="MonsterPhase.OnEntry"/> is: a
/// phase that drew from the roller would insert a die into the round and shift every hard-coded
/// SequenceDiceRoller script in the suite at once.
/// </param>
public sealed record HuntArchetype(
    string Key,
    string Noun,
    string Blurb,
    int HitPointsPercent,
    int HitPointsPerSubtask,
    int ArmourClassStep,
    int AttackBonusStep,
    int DropChanceStep,
    IReadOnlyList<LootEntry> LootTable,
    IReadOnlyList<MonsterPhase>? Phases = null);

/// <summary>Code-held, following DEC-004. Only the key is ever persisted.</summary>
public static class HuntArchetypeCatalog
{
    public const string Drudge = "hunt-drudge";
    public const string Tangle = "hunt-tangle";
    public const string Bulwark = "hunt-bulwark";
    public const string Hydra = "hunt-hydra";
    public const string Dread = "hunt-dread";

    /// <summary>
    /// The age at which a contract promotes its quarry, once and only once.
    /// </summary>
    /// <remarks>
    /// The same threshold the bounty caps at, on purpose. Past a month there is nothing left to
    /// gain by waiting: the purse has stopped growing and the only thing still changing is that
    /// the fight got harder. Two different thresholds would leave a window in which stalling
    /// still paid for itself.
    /// </remarks>
    public const int PromotionDays = 30;

    public static IReadOnlyList<HuntArchetype> All { get; } =
    [
        new(Drudge, "Drudge",
            "It is not dangerous. It is just always there.",
            HitPointsPercent: 70, HitPointsPerSubtask: 0,
            ArmourClassStep: -1, AttackBonusStep: 0, DropChanceStep: 0,
            [
                new LootEntry(ItemCatalog.WornDagger, 3),
                new LootEntry(ItemCatalog.PaddedJerkin, 3),
                new LootEntry(ItemCatalog.ThrowingKnives, 2),
                new LootEntry(ItemCatalog.OilskinCloak, 2),
                new LootEntry(ItemCatalog.LuckyCoin, 1)
            ]),

        new(Tangle, "Tangle",
            "A knot of smaller things. Pull on one and the rest arrive.",
            HitPointsPercent: 100, HitPointsPerSubtask: 3,
            ArmourClassStep: 0, AttackBonusStep: 0, DropChanceStep: 5,
            [
                new LootEntry(ItemCatalog.MilitiaSpear, 3),
                new LootEntry(ItemCatalog.HideHarness, 3),
                new LootEntry(ItemCatalog.QuickstringBracer, 2),
                new LootEntry(ItemCatalog.TumblersSash, 2),
                new LootEntry(ItemCatalog.IronBand, 1)
            ],
            // The only thing in the game that gets easier partway down. A job with pieces stops
            // fighting back once it is open, and the fiction and the mechanic agree about that.
            Phases:
            [
                new MonsterPhase(50, "Unravelled",
                    "The Tangle comes apart into pieces, and the pieces are smaller than the whole.",
                    [new StatusEffect(
                        EffectKind.Weakened, EffectTarget.Monster, StatusEffects.Lasting, 0,
                        Tangle)])
            ]),

        new(Bulwark, "Bulwark",
            "No handle, no edge, no obvious front. It does not have to do anything.",
            HitPointsPercent: 150, HitPointsPerSubtask: 0,
            ArmourClassStep: 3, AttackBonusStep: -1, DropChanceStep: 5,
            [
                new LootEntry(ItemCatalog.TowerShield, 3),
                new LootEntry(ItemCatalog.RingmailVest, 3),
                new LootEntry(ItemCatalog.BulwarkHalberd, 2),
                new LootEntry(ItemCatalog.OxhideBelt, 2),
                new LootEntry(ItemCatalog.QuarrymansGauntlets, 1)
            ],
            // Hardest thing on the board to hit and the worst at hitting back, which is the task
            // you cannot start written as a stat block: not a threat, just an immovable afternoon.
            Phases:
            [
                new MonsterPhase(60, "Seamed",
                    "A seam opens down the face of the Bulwark. There is a way in after all.",
                    [new StatusEffect(
                        EffectKind.Weakened, EffectTarget.Monster, StatusEffects.Lasting, 0,
                        Bulwark)])
            ]),

        new(Hydra, "Hydra",
            "You count the heads twice and get two different answers.",
            HitPointsPercent: 120, HitPointsPerSubtask: 5,
            ArmourClassStep: 0, AttackBonusStep: 0, DropChanceStep: 10,
            [
                new LootEntry(ItemCatalog.IronFlail, 3),
                new LootEntry(ItemCatalog.BeardedAxe, 3),
                new LootEntry(ItemCatalog.Brigandine, 2),
                new LootEntry(ItemCatalog.WayfarersCoat, 2),
                new LootEntry(ItemCatalog.HeartwoodToken, 1)
            ],
            // Both heads grant the same lasting Empowered, and the second refreshes rather than
            // stacks: StatusEffects.Apply takes an incoming effect only when it lasts at least as
            // long, and Lasting is Lasting. That is the archetype and not an oversight. A task
            // split into nine parts should be a long hunt, not a lethal one, and magnitudes that
            // stacked per head are how a ten-part chore becomes unsurvivable.
            Phases:
            [
                new MonsterPhase(66, "Doubled",
                    "The Hydra finds a second head and puts it to work.",
                    [new StatusEffect(
                        EffectKind.Empowered, EffectTarget.Monster, StatusEffects.Lasting, 1,
                        Hydra)]),

                new MonsterPhase(33, "Redoubled",
                    "Another head. You are no longer confident the count is finite.",
                    [new StatusEffect(
                        EffectKind.Empowered, EffectTarget.Monster, StatusEffects.Lasting, 1,
                        Hydra)])
            ]),

        new(Dread, "Dread",
            "Nobody saw it arrive. It has acquired a name since.",
            HitPointsPercent: 175, HitPointsPerSubtask: 6,
            ArmourClassStep: 2, AttackBonusStep: 1, DropChanceStep: 15,
            [
                new LootEntry(ItemCatalog.GravewatchPlate, 3),
                new LootEntry(ItemCatalog.OathkeepersMaul, 3),
                new LootEntry(ItemCatalog.SilveredBlade, 2),
                new LootEntry(ItemCatalog.ShadowweaveCloak, 2),
                new LootEntry(ItemCatalog.PendantOfTheBear, 2),
                new LootEntry(ItemCatalog.AugursBeads, 1),
                new LootEntry(ItemCatalog.CircletOfClarity, 1)
            ],
            // Unreachable as a base shape. Every Dread on the board is a task somebody left for a
            // month, which is what makes it a legend rather than another tier.
            Phases:
            [
                new MonsterPhase(50, "Feeding",
                    "The Dread takes back what you have done to it, in no particular hurry.",
                    [new StatusEffect(
                        EffectKind.Regenerating, EffectTarget.Monster, StatusEffects.Lasting, 2,
                        Dread)]),

                new MonsterPhase(25, "Legend",
                    "The Dread stops being a chore and starts being a story.",
                    [new StatusEffect(
                        EffectKind.Empowered, EffectTarget.Monster, StatusEffects.Lasting, 3,
                        Dread)])
            ])
    ];

    private static readonly Dictionary<string, HuntArchetype> ByKey =
        All.ToDictionary(a => a.Key, StringComparer.Ordinal);

    public static HuntArchetype? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);

    /// <summary>
    /// Which shape a task takes, from two axes and the calendar. A pure lookup, no dice.
    /// </summary>
    /// <remarks>
    /// Called once, when the contract is taken, and frozen in the encounter's MonsterKey from
    /// then on. Re-derived on the read path instead, editing a task's difficulty or adding a
    /// subtask would rename the monster mid-fight and move its maximum hit points underneath a
    /// health bar that is already drawn.
    /// <para>
    /// Takes no <c>IDiceRoller</c>, the rule <c>ShapeFor</c> and
    /// <see cref="MonsterPhase.OnEntry"/> already obey: a die spent here would land before the
    /// round's attack roll and silently change what every seeded script in the suite asserts.
    /// </para>
    /// </remarks>
    /// <param name="subtaskCount">Subtasks at the moment the contract was taken, not now.</param>
    /// <param name="daysOverdue">Days past due at that same moment, and never negative.</param>
    public static HuntArchetype ShapeFor(Difficulty difficulty, int subtaskCount, int daysOverdue)
    {
        var shape = BaseShape(difficulty, subtaskCount);

        return ByKey[daysOverdue >= PromotionDays ? Promoted(shape) : shape];
    }

    /// <summary>The shape before age is considered. Dread is not reachable from here.</summary>
    private static string BaseShape(Difficulty difficulty, int subtaskCount) => subtaskCount switch
    {
        <= 0 => difficulty >= Difficulty.Hard ? Bulwark : Drudge,
        <= 3 => Tangle,
        _ => Hydra
    };

    /// <summary>
    /// One step, at <see cref="PromotionDays"/>, and never again.
    /// </summary>
    /// <remarks>
    /// Dread promotes to itself, which is the ladder terminating rather than an omission. A rung
    /// above it would mean a task left for a year outranked a task left for a month, and the
    /// bounty cap exists precisely to say that it does not.
    /// </remarks>
    private static string Promoted(string key) => key switch
    {
        Drudge => Bulwark,
        Tangle => Hydra,
        Bulwark => Dread,
        Hydra => Dread,
        _ => key
    };

    /// <summary>
    /// The adjective a contract's age earns it, or null when the task is not overdue at all.
    /// </summary>
    /// <remarks>
    /// The half of the name that carries the age, and a catalog string chosen by a number of
    /// days rather than anything the user typed. That is what keeps the combat log, the
    /// chronicle and EncounterDto.MonsterName free of user text however a task was titled.
    /// </remarks>
    public static string? Epithet(int daysOverdue) => daysOverdue switch
    {
        <= 0 => null,
        <= 2 => "Nagging",
        <= 6 => "Lingering",
        <= 13 => "Festering",
        <= 29 => "Entrenched",
        <= 89 => "Ancient",
        _ => "Immemorial"
    };
}
