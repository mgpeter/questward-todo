namespace TodoApp.Models.Rpg;

/// <summary>What an objective counts. The target's meaning depends on the kind.</summary>
public enum ObjectiveKind
{
    /// <summary>Target is a monster key, or empty for any monster.</summary>
    DefeatMonster = 0,

    /// <summary>Target is a difficulty name, or empty for any task.</summary>
    CompleteTask = 1,

    /// <summary>Target is ignored; counts items acquired.</summary>
    AcquireItem = 2,

    /// <summary>Target is ignored; counts gold earned.</summary>
    EarnGold = 3,

    /// <summary>
    /// Target is a monster key, or empty for any monster. Counts a monster met for the
    /// first time, not a monster killed, so a fight fled from still counts.
    /// </summary>
    /// <remarks>
    /// Derived, not counted: QuestService reads the count straight off the bestiary rows
    /// rather than keeping a stored counter. A counter only moved while the quest was
    /// already unlocked, and a kind is met for the first time exactly once, so every
    /// sighting made below the quest's MinimumLevel was lost for good. Anything that adds a
    /// second discovery objective inherits that, and inherits the derivation with it.
    /// </remarks>
    DiscoverMonster = 4,

    /// <summary>
    /// Target is a faction banner key, or empty for any hunt. Counts contracts won.
    /// </summary>
    /// <remarks>
    /// A banner key and not an archetype key, which is what this used to say and what no site ever
    /// recorded. The two key spaces are disjoint, so an objective written against the old wording
    /// would have sat at zero forever with nothing anywhere to report it; the archetype is counted
    /// through <see cref="DefeatMonster"/> instead, because for a hunt the encounter's MonsterKey
    /// is the archetype key. CatalogIntegrityTests holds this contract to the catalog.
    /// <para>
    /// An ordinary counter recorded through RecordAsync, unlike <see cref="DiscoverMonster"/>
    /// beside it: a win is repeatable, so nothing is lost by only counting from the moment the
    /// quest unlocked and there is no reason to derive it.
    /// </para>
    /// <para>
    /// It must be recorded at the single site that sets EncounterStatus.Won and nowhere else,
    /// for the reason DiscoverMonster is recorded only inside StartAsync: the only way to reach
    /// it is then to have spent stamina on a fight, so completing a task cannot pay a quest by
    /// any route and DEC-014's gate stays the single answer to "may this pay out?".
    /// </para>
    /// </remarks>
    WinHunt = 5
}

/// <param name="Id">Stable within the quest; it is the key in the persisted counter map.</param>
public sealed record QuestObjective(
    string Id,
    ObjectiveKind Kind,
    string Target,
    int Required,
    string Description);

/// <param name="RewardItemKey">Optional guaranteed item, on top of the gold.</param>
public sealed record QuestDefinition(
    string Key,
    string Name,
    string Description,
    int MinimumLevel,
    IReadOnlyList<QuestObjective> Objectives,
    int RewardGold,
    string? RewardItemKey = null);

/// <summary>
/// Code-held, mirroring <see cref="Progression.AchievementCatalog"/>. Only progress
/// counters are persisted, so adding a quest never needs a migration.
/// </summary>
/// <remarks>
/// Quests grant gold and items. They never grant XP, because XP is reserved for real work
/// (DEC-003) and a quest that pays experience would make the todo list optional.
/// </remarks>
public static class QuestCatalog
{
    public const string FirstBlood = "first-hunt";
    public const string GoblinCull = "goblin-cull";
    public const string HonestWork = "honest-work";
    public const string BoneCollector = "bone-collector";
    public const string HeavyLifting = "heavy-lifting";
    public const string Treasurer = "treasurer";
    public const string WellEquipped = "well-equipped";
    public const string ApexPredator = "apex-predator";

    // Phase 4. Each of these is reachable at the level it unlocks: a quest naming a monster
    // of level N carries MinimumLevel N-1 or higher, because MonsterDefinition.IsAvailableAt
    // offers that monster to characters N-1 through N+2 and a quest asking for a fight the
    // tavern will not start is a dead entry on the board.
    //
    // The discovery quests need the other half of that arithmetic, the upper edge of the same
    // band. Once a character is at level 8 the tavern will not offer anything below monster
    // level 6 ever again, so a discovery total that only started counting at level 8 could
    // never be reached. They are reachable because progress is derived from the bestiary and
    // therefore counts kinds met at any level, at any time, including before the quest
    // unlocked. CatalogIntegrityTests measures the total against the whole ladder from level
    // one, which is the only reading under which these numbers are honest.
    public const string RatCatcher = "rat-catcher";
    public const string FieldNotes = "field-notes";
    public const string SteadyHands = "steady-hands";
    public const string CarrionWatch = "carrion-watch";
    public const string RoadTax = "road-tax";
    public const string StillWater = "still-water";
    public const string StrangeCompany = "strange-company";
    public const string PackHunt = "pack-hunt";
    public const string TheToll = "the-toll";
    public const string NoRelief = "no-relief";
    public const string Quartermaster = "quartermaster";
    public const string CoinCounter = "coin-counter";
    public const string BorderDispute = "border-dispute";
    public const string FairPrice = "fair-price";
    public const string ColdRooms = "cold-rooms";
    public const string DeepBreath = "deep-breath";
    public const string StandingPost = "standing-post";
    public const string FullCatalogue = "full-catalogue";
    public const string SaltAndOar = "salt-and-oar";
    public const string WellArmed = "well-armed";
    public const string FirstDragon = "first-dragon";
    public const string Statuary = "statuary";
    public const string PoorRelation = "poor-relation";
    public const string Grown = "grown";
    public const string LongService = "long-service";

    // Quests that answer back to the task list rather than to the tavern. Before these, 24 of
    // 33 quests were gated on combat, none targeted Medium, and WinHunt was declared and used
    // by nothing at all.
    public const string PlainWork = "plain-work";
    public const string MiddlingLabour = "middling-labour";
    public const string TheLongMiddle = "the-long-middle";
    public const string HouseInOrder = "house-in-order";
    public const string OnTheBooks = "on-the-books";
    public const string BodyOfWork = "body-of-work";
    public const string ReadingRoom = "reading-room";
    public const string BooksBalanced = "books-balanced";
    public const string FiveBanners = "five-banners";
    public const string DebtCollector = "debt-collector";
    public const string ArrearsAndErrands = "arrears-and-errands";
    public const string TwoKindsOfWork = "two-kinds-of-work";
    public const string SteadyIncome = "steady-income";
    public const string DoubleShift = "double-shift";
    public const string NothingLeftUndone = "nothing-left-undone";
    public const string TheReckoning = "the-reckoning";

    public static IReadOnlyList<QuestDefinition> All { get; } =
    [
        new(FirstBlood, "First Hunt",
            "Everyone starts somewhere. Usually with something small and unpleasant.",
            MinimumLevel: 1,
            [new QuestObjective("any", ObjectiveKind.DefeatMonster, "", 1, "Defeat any monster")],
            RewardGold: 15),

        new(GoblinCull, "Goblin Cull",
            "The road has become impassable. Do something about it.",
            MinimumLevel: 1,
            [new QuestObjective("goblins", ObjectiveKind.DefeatMonster, MonsterCatalog.Goblin, 3, "Defeat 3 goblins")],
            RewardGold: 40, RewardItemKey: ItemCatalog.GoblinCleaver),

        new(HonestWork, "Honest Work",
            "The stamina to fight comes from somewhere. Go and earn it.",
            MinimumLevel: 1,
            [new QuestObjective("tasks", ObjectiveKind.CompleteTask, "", 5, "Complete 5 tasks")],
            RewardGold: 25),

        new(HeavyLifting, "Heavy Lifting",
            "Not everything on the list is small.",
            MinimumLevel: 2,
            [new QuestObjective("hard", ObjectiveKind.CompleteTask, "hard", 3, "Complete 3 Hard tasks")],
            RewardGold: 60, RewardItemKey: ItemCatalog.RingOfVigour),

        new(BoneCollector, "Bone Collector",
            "The crypt keeps refilling. Nobody knows why.",
            MinimumLevel: 2,
            [new QuestObjective("skeletons", ObjectiveKind.DefeatMonster, MonsterCatalog.Skeleton, 4, "Defeat 4 skeletons")],
            RewardGold: 70, RewardItemKey: ItemCatalog.AmuletOfInsight),

        new(Treasurer, "Treasurer",
            "Coin has a way of accumulating once you start paying attention.",
            MinimumLevel: 2,
            [new QuestObjective("gold", ObjectiveKind.EarnGold, "", 250, "Earn 250 gold")],
            RewardGold: 100),

        new(WellEquipped, "Well Equipped",
            "A collection, of sorts.",
            MinimumLevel: 3,
            [new QuestObjective("items", ObjectiveKind.AcquireItem, "", 8, "Acquire 8 items")],
            RewardGold: 90, RewardItemKey: ItemCatalog.BootsOfSpeed),

        new(ApexPredator, "Apex Predator",
            "Something large has moved into the valley.",
            MinimumLevel: 5,
            [
                new QuestObjective("ogres", ObjectiveKind.DefeatMonster, MonsterCatalog.Ogre, 2, "Defeat 2 ogres"),
                new QuestObjective("epics", ObjectiveKind.CompleteTask, "epic", 1, "Complete 1 Epic task")
            ],
            RewardGold: 200, RewardItemKey: ItemCatalog.ScaleMail),

        // Phase 4 quests, ordered by MinimumLevel so a reader can see the ladder. Discovery
        // counts are cumulative rather than per monster, so deleting a monster later shortens
        // the bestiary without stranding a quest at an unreachable total.
        new(RatCatcher, "Rat Catcher",
            "Something in the granary is eating better than you are.",
            MinimumLevel: 1,
            [new QuestObjective("rats", ObjectiveKind.DefeatMonster, MonsterCatalog.GiantRat, 5, "Defeat 5 giant rats")],
            RewardGold: 20),

        new(FieldNotes, "Field Notes",
            "Nobody wrote down what lives out here. Somebody should.",
            MinimumLevel: 1,
            [new QuestObjective("seen", ObjectiveKind.DiscoverMonster, "", 3, "Discover 3 kinds of monster")],
            RewardGold: 30, RewardItemKey: ItemCatalog.CartographersLens),

        new(SteadyHands, "Steady Hands",
            "Small things first. The larger ones will keep.",
            MinimumLevel: 1,
            [new QuestObjective("easy", ObjectiveKind.CompleteTask, "easy", 10, "Complete 10 Easy tasks")],
            RewardGold: 30),

        new(CarrionWatch, "Carrion Watch",
            "They have started following you in particular. Discourage them.",
            MinimumLevel: 2,
            [new QuestObjective("crows", ObjectiveKind.DefeatMonster, MonsterCatalog.CarrionCrows, 3, "Defeat 3 carrion flocks")],
            RewardGold: 55, RewardItemKey: ItemCatalog.OilskinCloak),

        new(RoadTax, "Road Tax",
            "Somebody has appointed himself to the road.",
            MinimumLevel: 2,
            [new QuestObjective("bandits", ObjectiveKind.DefeatMonster, MonsterCatalog.Bandit, 3, "Defeat 3 bandits")],
            RewardGold: 80, RewardItemKey: ItemCatalog.WayfarersCoat),

        new(StillWater, "Still Water",
            "The fen looks empty. The fen is not empty.",
            MinimumLevel: 3,
            [new QuestObjective("toads", ObjectiveKind.DefeatMonster, MonsterCatalog.MireToad, 3, "Defeat 3 mire toads")],
            RewardGold: 85, RewardItemKey: ItemCatalog.BoarSpear),

        new(StrangeCompany, "Strange Company",
            "Half the valley has never been looked at properly.",
            MinimumLevel: 3,
            [new QuestObjective("seen", ObjectiveKind.DiscoverMonster, "", 6, "Discover 6 kinds of monster")],
            RewardGold: 70, RewardItemKey: ItemCatalog.HeartwoodToken),

        new(PackHunt, "Pack Hunt",
            "The shepherd has stopped counting. Not out of laziness.",
            MinimumLevel: 3,
            [new QuestObjective("wolves", ObjectiveKind.DefeatMonster, MonsterCatalog.DireWolf, 3, "Defeat 3 dire wolves")],
            RewardGold: 95, RewardItemKey: ItemCatalog.QuickstringBracer),

        new(TheToll, "The Toll",
            "The bridge is free to cross. The troll disagrees.",
            MinimumLevel: 4,
            [new QuestObjective("trolls", ObjectiveKind.DefeatMonster, MonsterCatalog.HedgeTroll, 2, "Defeat 2 hedge trolls")],
            RewardGold: 120, RewardItemKey: ItemCatalog.OxhideBelt),

        new(NoRelief, "No Relief",
            "The quarry is not a posting. He has made it one.",
            MinimumLevel: 4,
            [new QuestObjective("deserters", ObjectiveKind.DefeatMonster, MonsterCatalog.Deserter, 2, "Defeat 2 deserters")],
            RewardGold: 120, RewardItemKey: ItemCatalog.Brigandine),

        new(Quartermaster, "Quartermaster",
            "The pack is heavier than it was. It gets heavier.",
            MinimumLevel: 5,
            [new QuestObjective("items", ObjectiveKind.AcquireItem, "", 15, "Acquire 15 items")],
            RewardGold: 150, RewardItemKey: ItemCatalog.ChainHauberk),

        new(CoinCounter, "Coin Counter",
            "A purse that no longer folds flat.",
            MinimumLevel: 5,
            [new QuestObjective("gold", ObjectiveKind.EarnGold, "", 1_000, "Earn 1000 gold")],
            RewardGold: 250),

        new(BorderDispute, "Border Dispute",
            "The county line moved. It did not.",
            MinimumLevel: 6,
            [new QuestObjective("sentinels", ObjectiveKind.DefeatMonster, MonsterCatalog.StoneSentinel, 2, "Defeat 2 stone sentinels")],
            RewardGold: 200, RewardItemKey: ItemCatalog.QuarrymansGauntlets),

        new(FairPrice, "Fair Price",
            "Nobody who dealt with her was cheated.",
            MinimumLevel: 6,
            [new QuestObjective("hags", ObjectiveKind.DefeatMonster, MonsterCatalog.FenHag, 2, "Defeat 2 fen hags")],
            RewardGold: 220, RewardItemKey: ItemCatalog.RingOfFocus),

        new(ColdRooms, "Cold Rooms",
            "The east wing has been cold since the funeral.",
            MinimumLevel: 7,
            [new QuestObjective("wraiths", ObjectiveKind.DefeatMonster, MonsterCatalog.Wraith, 3, "Defeat 3 wraiths")],
            RewardGold: 260, RewardItemKey: ItemCatalog.CircletOfClarity),

        new(DeepBreath, "Deep Breath",
            "Some things are not finished in an afternoon.",
            MinimumLevel: 7,
            [new QuestObjective("epics", ObjectiveKind.CompleteTask, "epic", 3, "Complete 3 Epic tasks")],
            RewardGold: 300, RewardItemKey: ItemCatalog.TemplarsCuirass),

        new(StandingPost, "Standing Post",
            "He was buried on duty and never told otherwise.",
            MinimumLevel: 8,
            [new QuestObjective("knights", ObjectiveKind.DefeatMonster, MonsterCatalog.BarrowKnight, 2, "Defeat 2 barrow knights")],
            RewardGold: 320, RewardItemKey: ItemCatalog.GravewatchPlate),

        new(FullCatalogue, "Full Catalogue",
            "Most of the countryside is named now. Most is not all.",
            MinimumLevel: 8,
            [new QuestObjective("seen", ObjectiveKind.DiscoverMonster, "", 12, "Discover 12 kinds of monster")],
            RewardGold: 350, RewardItemKey: ItemCatalog.PhilosophersInkstone),

        new(SaltAndOar, "Salt and Oar",
            "The boat came back. So did everyone aboard it.",
            MinimumLevel: 9,
            [new QuestObjective("crews", ObjectiveKind.DefeatMonster, MonsterCatalog.DrownedCrew, 2, "Defeat 2 drowned crews")],
            RewardGold: 400, RewardItemKey: ItemCatalog.LodestoneSceptre),

        new(WellArmed, "Well Armed",
            "Enough armour now to be choosy about it.",
            MinimumLevel: 9,
            [new QuestObjective("items", ObjectiveKind.AcquireItem, "", 30, "Acquire 30 items")],
            RewardGold: 380, RewardItemKey: ItemCatalog.DuellistsHalfPlate),

        new(FirstDragon, "First Dragon",
            "It has learned that livestock is easier than deer.",
            MinimumLevel: 10,
            [new QuestObjective("dragon", ObjectiveKind.DefeatMonster, MonsterCatalog.YoungDragon, 1, "Defeat 1 young dragon")],
            RewardGold: 450, RewardItemKey: ItemCatalog.DragonfangSpear),

        new(Statuary, "Statuary",
            "The den is full of statues nobody commissioned.",
            MinimumLevel: 11,
            [
                new QuestObjective("basilisks", ObjectiveKind.DefeatMonster, MonsterCatalog.Basilisk, 2, "Defeat 2 basilisks"),
                new QuestObjective("hard", ObjectiveKind.CompleteTask, "hard", 5, "Complete 5 Hard tasks")
            ],
            RewardGold: 500, RewardItemKey: ItemCatalog.TowerShield),

        new(PoorRelation, "Poor Relation",
            "Something is taking lambs and it is not the dragon.",
            MinimumLevel: 12,
            [new QuestObjective("wyverns", ObjectiveKind.DefeatMonster, MonsterCatalog.Wyvern, 2, "Defeat 2 wyverns")],
            RewardGold: 600, RewardItemKey: ItemCatalog.LongbowOfTheVale),

        new(Grown, "Grown",
            "It has outgrown the passes and stayed anyway.",
            MinimumLevel: 13,
            [new QuestObjective("elder", ObjectiveKind.DefeatMonster, MonsterCatalog.ElderDragon, 1, "Defeat 1 elder dragon")],
            RewardGold: 900, RewardItemKey: ItemCatalog.BreastplateOfDawn),

        new(LongService, "Long Service",
            "Nearly everything out there, seen once and written down.",
            MinimumLevel: 14,
            [
                new QuestObjective("seen", ObjectiveKind.DiscoverMonster, "", 18, "Discover 18 kinds of monster"),
                new QuestObjective("gold", ObjectiveKind.EarnGold, "", 5_000, "Earn 5000 gold")
            ],
            RewardGold: 1_200, RewardItemKey: ItemCatalog.OathkeepersMaul),

        // ------------------------------------------------------------------ task work
        //
        // The board used to be almost entirely combat: finishing an actual task advanced six
        // quests out of thirty-three, and the difficulty most people use most often, Medium,
        // advanced none of them. These pay for the work the app is actually for.

        new(PlainWork, "Plain Work",
            "Not every job is a saga. Most of them are Tuesday.",
            MinimumLevel: 1,
            [new QuestObjective("medium", ObjectiveKind.CompleteTask, "medium", 5, "Complete 5 Medium tasks")],
            RewardGold: 30),

        new(MiddlingLabour, "Middling Labour",
            "Neither trivial nor terrible. The bulk of a life, really.",
            MinimumLevel: 3,
            [new QuestObjective("medium", ObjectiveKind.CompleteTask, "medium", 15, "Complete 15 Medium tasks")],
            RewardGold: 90,
            RewardItemKey: ItemCatalog.TravellersCloak),

        new(TheLongMiddle, "The Long Middle",
            "Nobody writes songs about the middle. It still has to be crossed.",
            MinimumLevel: 7,
            [new QuestObjective("medium", ObjectiveKind.CompleteTask, "medium", 40, "Complete 40 Medium tasks")],
            RewardGold: 260),

        new(TwoKindsOfWork, "Two Kinds of Work",
            "One sort you can put off. The other sort puts up a fight.",
            MinimumLevel: 4,
            [
                new QuestObjective("tasks", ObjectiveKind.CompleteTask, "", 10, "Complete 10 tasks"),
                new QuestObjective("kills", ObjectiveKind.DefeatMonster, "", 5, "Win 5 fights")
            ],
            RewardGold: 120),

        new(DoubleShift, "Double Shift",
            "The hard ones and the long ones are not the same ones.",
            MinimumLevel: 6,
            [
                new QuestObjective("medium", ObjectiveKind.CompleteTask, "medium", 12, "Complete 12 Medium tasks"),
                new QuestObjective("hard", ObjectiveKind.CompleteTask, "hard", 4, "Complete 4 Hard tasks")
            ],
            RewardGold: 200),

        new(NothingLeftUndone, "Nothing Left Undone",
            "Every size of job, one after another, until the list is quiet.",
            MinimumLevel: 8,
            [
                new QuestObjective("easy", ObjectiveKind.CompleteTask, "easy", 10, "Complete 10 Easy tasks"),
                new QuestObjective("medium", ObjectiveKind.CompleteTask, "medium", 10, "Complete 10 Medium tasks"),
                new QuestObjective("hard", ObjectiveKind.CompleteTask, "hard", 5, "Complete 5 Hard tasks")
            ],
            RewardGold: 320,
            RewardItemKey: ItemCatalog.RingOfTheDiligent),

        // ------------------------------------------------------------------- contracts
        //
        // WinHunt, which was declared for exactly this and then used by nothing. It is the
        // strongest link there is between a quest and real work: a contract exists only
        // because a task of yours went overdue, and its banner is chosen from your own tags.

        new(DebtCollector, "Debt Collector",
            "Something on your list has been waiting long enough to have grown a name.",
            MinimumLevel: 2,
            [new QuestObjective("hunts", ObjectiveKind.WinHunt, "", 1, "Collect on 1 contract")],
            RewardGold: 60),

        new(ArrearsAndErrands, "Arrears and Errands",
            "They do not get smaller while you look at them.",
            MinimumLevel: 5,
            [new QuestObjective("hunts", ObjectiveKind.WinHunt, "", 5, "Collect on 5 contracts")],
            RewardGold: 180),

        new(TheReckoning, "The Reckoning",
            "A backlog, met one piece at a time, is only ever a list.",
            MinimumLevel: 10,
            [new QuestObjective("hunts", ObjectiveKind.WinHunt, "", 15, "Collect on 15 contracts")],
            RewardGold: 450,
            RewardItemKey: ItemCatalog.LedgerOfDebts),

        // One per banner. The banner comes from the task's own tags, so which of these a
        // player can even start is decided by how they label their own work.

        new(HouseInOrder, "House in Order",
            "The Hearth keeps the kind of accounts nobody writes down.",
            MinimumLevel: 3,
            [new QuestObjective("hearth", ObjectiveKind.WinHunt, FactionCatalog.TheHearth, 3, "Collect on 3 contracts for the Hearth")],
            RewardGold: 110),

        new(OnTheBooks, "On the Books",
            "The Ledger notices what was promised and what arrived.",
            MinimumLevel: 3,
            [new QuestObjective("ledger", ObjectiveKind.WinHunt, FactionCatalog.TheLedger, 3, "Collect on 3 contracts for the Ledger")],
            RewardGold: 110),

        new(BodyOfWork, "Body of Work",
            "The Vigil is unimpressed by intentions.",
            MinimumLevel: 3,
            [new QuestObjective("vigil", ObjectiveKind.WinHunt, FactionCatalog.TheVigil, 3, "Collect on 3 contracts for the Vigil")],
            RewardGold: 110),

        new(ReadingRoom, "Reading Room",
            "The Athenaeum has a great deal of shelf and very little patience.",
            MinimumLevel: 3,
            [new QuestObjective("athenaeum", ObjectiveKind.WinHunt, FactionCatalog.TheAthenaeum, 3, "Collect on 3 contracts for the Athenaeum")],
            RewardGold: 110),

        new(BooksBalanced, "Books Balanced",
            "The Exchequer can wait. It would rather not.",
            MinimumLevel: 3,
            [new QuestObjective("exchequer", ObjectiveKind.WinHunt, FactionCatalog.TheExchequer, 3, "Collect on 3 contracts for the Exchequer")],
            RewardGold: 110),

        new(FiveBanners, "Five Banners",
            "Every part of a life has somebody keeping score of it.",
            MinimumLevel: 9,
            [
                new QuestObjective("hearth", ObjectiveKind.WinHunt, FactionCatalog.TheHearth, 2, "Collect on 2 for the Hearth"),
                new QuestObjective("ledger", ObjectiveKind.WinHunt, FactionCatalog.TheLedger, 2, "Collect on 2 for the Ledger"),
                new QuestObjective("vigil", ObjectiveKind.WinHunt, FactionCatalog.TheVigil, 2, "Collect on 2 for the Vigil"),
                new QuestObjective("athenaeum", ObjectiveKind.WinHunt, FactionCatalog.TheAthenaeum, 2, "Collect on 2 for the Athenaeum"),
                new QuestObjective("exchequer", ObjectiveKind.WinHunt, FactionCatalog.TheExchequer, 2, "Collect on 2 for the Exchequer")
            ],
            RewardGold: 500,
            RewardItemKey: ItemCatalog.BannerbearersTorc),

        new(SteadyIncome, "Steady Income",
            "Gold from work, gold from teeth. It spends the same.",
            MinimumLevel: 6,
            [
                new QuestObjective("hunts", ObjectiveKind.WinHunt, "", 3, "Collect on 3 contracts"),
                new QuestObjective("gold", ObjectiveKind.EarnGold, "", 400, "Earn 400 gold")
            ],
            RewardGold: 220),
    ];

    private static readonly Dictionary<string, QuestDefinition> ByKey =
        All.ToDictionary(q => q.Key, StringComparer.Ordinal);

    public static QuestDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);

    public static IReadOnlyList<QuestDefinition> AvailableAt(int level) =>
        All.Where(q => q.MinimumLevel <= level).ToList();
}
