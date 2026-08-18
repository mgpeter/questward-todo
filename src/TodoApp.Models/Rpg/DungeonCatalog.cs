namespace TodoApp.Models.Rpg;

/// <param name="Weight">Relative chance within the pool. Not a percentage; weights are summed.</param>
public sealed record RoomEntry(string MonsterKey, int Weight);

/// <summary>
/// A run of rooms ending in a named boss.
/// </summary>
/// <remarks>
/// Code-held with the rest of the game's content (DEC-004). A run persists the keys it rolled
/// and nothing else, so retuning a dungeon's reward or renaming a room retunes every run in
/// progress at once.
/// </remarks>
/// <param name="Level">The character level the dungeon unlocks at.</param>
/// <param name="Rooms">
/// How many fights a run is, the boss included. The last room is always the boss, which is what
/// wires boss phases to dungeons.
/// </param>
/// <param name="Pool">Drawn from once per non-boss room, by weight.</param>
/// <param name="ClearGold">Paid once, on the boss's death, on top of that fight's own loot.</param>
/// <param name="RewardFloor">
/// The worst the guaranteed clear drop is allowed to be. A floor rather than a fixed rarity, so a
/// lucky roll still beats it and the deepest dungeon is never worse than the shallowest.
/// </param>
public sealed record DungeonDefinition(
    string Key,
    string Name,
    string Blurb,
    int Level,
    int Rooms,
    IReadOnlyList<RoomEntry> Pool,
    string BossKey,
    int ClearGold,
    Rarity RewardFloor,
    IReadOnlyList<LootEntry> RewardTable)
{
    /// <summary>The thing in the last room, or null when the bestiary has moved on without it.</summary>
    public MonsterDefinition? Boss => MonsterCatalog.Find(BossKey);

    /// <summary>
    /// Unlocked at its level and never retired.
    /// </summary>
    /// <remarks>
    /// Deliberately a floor rather than the asymmetric band <see cref="MonsterDefinition.IsAvailableAt"/>
    /// uses, and the difference is worth stating because the two rules sit beside each other.
    /// <para>
    /// The band exists to keep an unwinnable opponent off the tavern board. It cannot mean that
    /// here: a dungeon's boss deliberately sits three levels above the dungeon's own gate, so a
    /// band read against the boss would refuse every dungeon to everybody. Read against the gate
    /// instead, a band would retire the Sunken Warren at level five and the Barrow Deeps at ten,
    /// which with three dungeons two bands apart leaves a level five and a level ten character
    /// with no dungeon at all. A floor has neither problem, and re-running a cleared dungeon for
    /// its reward table is a reason to keep it rather than a reason to hide it.
    /// </para>
    /// </remarks>
    public bool IsAvailableAt(int characterLevel) => characterLevel >= Level;
}

/// <summary>Code-held, following DEC-004. Only the key and the rolled chain are ever persisted.</summary>
public static class DungeonCatalog
{
    public const string SunkenWarren = "sunken-warren";
    public const string BarrowDeeps = "barrow-deeps";
    public const string DragonsReach = "dragons-reach";

    public static IReadOnlyList<DungeonDefinition> All { get; } =
    [
        new(SunkenWarren, "The Sunken Warren",
            "Somebody dug too enthusiastically, and then it rained for a decade.",
            Level: 2,
            Rooms: 3,
            [
                new RoomEntry(MonsterCatalog.GiantRat, 3),
                new RoomEntry(MonsterCatalog.Goblin, 3),
                new RoomEntry(MonsterCatalog.Skeleton, 3),
                new RoomEntry(MonsterCatalog.CarrionCrows, 2),
                new RoomEntry(MonsterCatalog.MireToad, 1)
            ],
            BossKey: MonsterCatalog.HedgeTroll,
            ClearGold: 60,
            RewardFloor: Rarity.Uncommon,
            [
                new LootEntry(ItemCatalog.GoblinCleaver, 3),
                new LootEntry(ItemCatalog.StuddedLeather, 3),
                new LootEntry(ItemCatalog.BoarSpear, 2),
                new LootEntry(ItemCatalog.HideHarness, 2),
                new LootEntry(ItemCatalog.IronBand, 2),
                new LootEntry(ItemCatalog.LuckyCoin, 1)
            ]),

        new(BarrowDeeps, "The Barrow Deeps",
            "The dead here were buried standing up, facing the door. That was not an accident.",
            Level: 7,
            Rooms: 4,
            [
                new RoomEntry(MonsterCatalog.Wraith, 3),
                new RoomEntry(MonsterCatalog.StoneSentinel, 3),
                new RoomEntry(MonsterCatalog.FenHag, 3),
                new RoomEntry(MonsterCatalog.Ogre, 2),
                new RoomEntry(MonsterCatalog.Deserter, 1)
            ],
            BossKey: MonsterCatalog.BarrowKnight,
            ClearGold: 250,
            RewardFloor: Rarity.Rare,
            [
                new LootEntry(ItemCatalog.SilveredBlade, 3),
                new LootEntry(ItemCatalog.GravewatchPlate, 3),
                new LootEntry(ItemCatalog.WardingShield, 2),
                new LootEntry(ItemCatalog.OathkeepersMaul, 2),
                new LootEntry(ItemCatalog.PendantOfTheBear, 2),
                new LootEntry(ItemCatalog.RingOfFocus, 1)
            ]),

        new(DragonsReach, "Dragon's Reach",
            "The last three expeditions sent word that the climb was the hard part.",
            Level: 12,
            Rooms: 5,
            [
                new RoomEntry(MonsterCatalog.Basilisk, 3),
                new RoomEntry(MonsterCatalog.YoungDragon, 3),
                new RoomEntry(MonsterCatalog.DrownedCrew, 3),
                new RoomEntry(MonsterCatalog.Wyvern, 2)
            ],
            BossKey: MonsterCatalog.ElderDragon,
            ClearGold: 750,
            RewardFloor: Rarity.Epic,
            [
                new LootEntry(ItemCatalog.DragonfangSpear, 3),
                new LootEntry(ItemCatalog.BreastplateOfDawn, 3),
                new LootEntry(ItemCatalog.LongbowOfTheVale, 2),
                new LootEntry(ItemCatalog.ShadowweaveCloak, 2),
                new LootEntry(ItemCatalog.AmuletOfInsight, 2),
                new LootEntry(ItemCatalog.CircletOfClarity, 1)
            ])
    ];

    private static readonly Dictionary<string, DungeonDefinition> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static DungeonDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);

    public static IReadOnlyList<DungeonDefinition> AvailableAt(int characterLevel) =>
        [.. All.Where(d => d.IsAvailableAt(characterLevel)).OrderBy(d => d.Level)];
}
