namespace TodoApp.Models.Progression;

/// <summary>
/// The badge catalog. Lives in code rather than the database so new achievements ship
/// without a migration; only the unlock rows are persisted.
/// </summary>
public static class AchievementCatalog
{
    public const string FirstBlood = "first-blood";
    public const string GettingStarted = "getting-started";
    public const string Centurion = "centurion";
    public const string EpicSlayer = "epic-slayer";
    public const string GiantKiller = "giant-killer";
    public const string DeepWork = "deep-work";
    public const string Level5 = "level-5";
    public const string Level10 = "level-10";
    public const string Level25 = "level-25";
    public const string CleanSlate = "clean-slate";
    public const string NightOwl = "night-owl";
    public const string EarlyBird = "early-bird";
    public const string ProductiveDay = "productive-day";

    public static IReadOnlyList<AchievementDefinition> All { get; } =
    [
        new(FirstBlood, "First Blood", "You completed your very first task.",
            "Complete your first task.", "\U0001F5E1️"),
        new(GettingStarted, "Getting Started", "Ten tasks down.",
            "Complete 10 tasks.", "\U0001F331"),
        new(Centurion, "Centurion", "One hundred tasks completed.",
            "Complete 100 tasks.", "\U0001F3DB️"),
        new(EpicSlayer, "Epic Slayer", "You took down an Epic task.",
            "Complete a task marked Epic.", "\U0001F409"),
        new(GiantKiller, "Giant Killer", "Ten Hard or Epic tasks completed.",
            "Complete 10 Hard or Epic tasks.", "⚔️"),
        new(DeepWork, "Deep Work", "You cleared a task worth 50 XP or more in one go.",
            "Complete a single task worth 50 XP or more.", "\U0001F9E0"),
        new(Level5, "Adept", "You reached level 5.",
            "Reach level 5.", "⭐"),
        new(Level10, "Double Digits", "You reached level 10.",
            "Reach level 10.", "\U0001F31F"),
        new(Level25, "Ascendant", "You reached level 25.",
            "Reach level 25.", "\U0001F320"),
        new(CleanSlate, "Clean Slate", "You emptied a list of three or more open tasks.",
            "Clear every open task while at least 3 are in play.", "\U0001F9F9"),
        new(NightOwl, "Night Owl", "A task completed between midnight and 4am.",
            "Complete a task between 00:00 and 04:00.", "\U0001F989"),
        new(EarlyBird, "Early Bird", "A task completed before 6am.",
            "Complete a task before 06:00.", "\U0001F423"),
        new(ProductiveDay, "Productive Day", "Five tasks completed in a single day.",
            "Complete 5 tasks in one day.", "\U0001F680")
    ];

    private static readonly Dictionary<string, AchievementDefinition> ByKeyLookup =
        All.ToDictionary(a => a.Key, StringComparer.Ordinal);

    public static AchievementDefinition? Find(string key) =>
        ByKeyLookup.TryGetValue(key, out var definition) ? definition : null;
}
