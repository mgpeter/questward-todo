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
    EarnGold = 3
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
            RewardGold: 200, RewardItemKey: ItemCatalog.ScaleMail)
    ];

    private static readonly Dictionary<string, QuestDefinition> ByKey =
        All.ToDictionary(q => q.Key, StringComparer.Ordinal);

    public static QuestDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);

    public static IReadOnlyList<QuestDefinition> AvailableAt(int level) =>
        All.Where(q => q.MinimumLevel <= level).ToList();
}
