using TodoApp.Models.Progression;

namespace TodoApp.Models.Rpg;

/// <summary>One entry as the board reads it: an icon key, a headline, and an optional second line.</summary>
/// <param name="Icon">
/// A key, not a glyph. The client owns which drawing it maps to, exactly as it owns the drawing
/// for an avatar key or a monster key, so changing the picture never touches the server.
/// </param>
public sealed record ChronicleLine(string Icon, string Title, string? Detail);

/// <summary>
/// Turns a stored entry's facts into the sentence the chronicle shows.
/// </summary>
/// <remarks>
/// Code-held per DEC-004: the rows carry catalog keys and numbers, and every word comes from here.
/// A reworded entry is a code change, and it rewords the whole history at once rather than only
/// the rows written after it.
/// <para>
/// Every lookup falls back rather than throwing. A fact can be missing because the kind gained it
/// after the row was written, and a key can be missing because a catalog dropped it; neither is a
/// reason for a chronicle to fail to render.
/// </para>
/// </remarks>
public static class ChronicleNarrator
{
    public const string IconFight = "fight";
    public const string IconDefeat = "defeat";
    public const string IconFlight = "flight";
    public const string IconQuest = "quest";
    public const string IconContract = "contract";
    public const string IconBanner = "banner";
    public const string IconDungeon = "dungeon";
    public const string IconLevel = "level";
    public const string IconAscend = "ascend";

    // Fact keys, named once so the writer and the reader cannot drift apart on a spelling.
    public const string MonsterKey = "monsterKey";
    public const string ItemKey = "itemKey";
    public const string RarityKey = "rarity";
    public const string QuestKey = "questKey";
    public const string DungeonKey = "dungeonKey";
    public const string FactionKey = "factionKey";
    public const string TaskTitleKey = "taskTitle";
    public const string RoundsKey = "rounds";
    public const string RoomsKey = "rooms";
    public const string DepthKey = "depth";
    public const string DaysOverdueKey = "daysOverdue";
    public const string WinsKey = "wins";
    public const string GoldKey = "gold";
    public const string StaminaKey = "stamina";
    public const string EssenceKey = "essence";
    public const string LevelKey = "level";
    public const string StandingKey = "standing";
    public const string OrdinalKey = "ordinal";

    public static ChronicleLine Narrate(ChronicleKind kind, IReadOnlyDictionary<string, string> facts)
    {
        var monster = Text(facts, MonsterKey) is { } key ? MonsterNames.Of(key) : "something";

        return kind switch
        {
            ChronicleKind.FightWon => new ChronicleLine(
                IconFight,
                $"Felled {Article(monster)}",
                Join(Rounds(facts), Gold(facts), Drop(facts))),

            ChronicleKind.FightLost => new ChronicleLine(
                IconDefeat,
                $"Fell to {Article(monster)}",
                Rounds(facts)),

            ChronicleKind.FightFled => new ChronicleLine(
                IconFlight,
                $"Withdrew from {Article(monster)}",
                Rounds(facts)),

            ChronicleKind.QuestClaimed => new ChronicleLine(
                IconQuest,
                $"Claimed \"{QuestName(facts)}\"",
                Join(Gold(facts), Drop(facts))),

            ChronicleKind.ContractAccepted => new ChronicleLine(
                IconContract,
                $"Took a contract on \"{Text(facts, TaskTitleKey) ?? "a task"}\"",
                Join(BannerName(facts) is { } banner ? $"For {banner}" : null, Overdue(facts))),

            ChronicleKind.ContractSettled => new ChronicleLine(
                IconContract,
                $"Settled the contract on \"{Text(facts, TaskTitleKey) ?? "a task"}\"",
                Join(BannerName(facts) is { } banner ? $"{banner} paid out" : null, Drop(facts))),

            ChronicleKind.StandingRaised => new ChronicleLine(
                IconBanner,
                Standing(facts),
                BannerName(facts) is { } banner ? $"{banner} counts {Wins(facts)}" : null),

            ChronicleKind.DungeonCleared => new ChronicleLine(
                IconDungeon,
                $"Cleared {DungeonName(facts)}",
                Join(Rooms(facts), Gold(facts), Drop(facts))),

            ChronicleKind.DungeonFailed => new ChronicleLine(
                IconDungeon,
                $"Failed in {DungeonName(facts)}",
                Depth(facts)),

            ChronicleKind.LevelReached => new ChronicleLine(
                IconLevel,
                $"Reached level {Number(facts, LevelKey)}",
                RankTitles.ForLevel(Number(facts, LevelKey))),

            ChronicleKind.Ascended => new ChronicleLine(
                IconAscend,
                $"Ascended at level {Number(facts, LevelKey)}",
                Ascension(facts)),

            _ => new ChronicleLine(IconLevel, "Something happened", null)
        };
    }

    /// <summary>The banner's own word for a hunter of the standing just reached.</summary>
    private static string Standing(IReadOnlyDictionary<string, string> facts)
    {
        var faction = FactionCatalog.Find(Text(facts, FactionKey));
        var standing = (FactionStanding)Number(facts, StandingKey);

        return faction is null
            ? "Your standing rose"
            : $"{faction.Name} now calls you {faction.TitleAt(standing)}";
    }

    private static string Ascension(IReadOnlyDictionary<string, string> facts)
    {
        var gold = Number(facts, GoldKey);
        var stamina = Number(facts, StaminaKey);
        var essence = Number(facts, EssenceKey);

        return $"{gold:N0} gold and {stamina:N0} stamina rendered down to {essence:N0} essence";
    }

    private static string? Drop(IReadOnlyDictionary<string, string> facts)
    {
        if (Text(facts, ItemKey) is not { } key)
        {
            return null;
        }

        var name = ItemCatalog.Find(key)?.Name ?? key;

        return Text(facts, RarityKey) is { } rarity && Enum.TryParse<Rarity>(rarity, true, out var parsed)
            ? $"{name} ({parsed})"
            : name;
    }

    private static string? Gold(IReadOnlyDictionary<string, string> facts) =>
        Number(facts, GoldKey) is var gold && gold > 0 ? $"{gold:N0} gold" : null;

    private static string? Rounds(IReadOnlyDictionary<string, string> facts) =>
        Number(facts, RoundsKey) is var rounds && rounds > 0
            ? $"{rounds} round{(rounds == 1 ? string.Empty : "s")}"
            : null;

    private static string? Rooms(IReadOnlyDictionary<string, string> facts) =>
        Number(facts, RoomsKey) is var rooms && rooms > 0 ? $"{rooms} rooms" : null;

    private static string? Depth(IReadOnlyDictionary<string, string> facts) =>
        Number(facts, DepthKey) is var depth && depth > 0
            ? $"{depth} room{(depth == 1 ? string.Empty : "s")} deep"
            : null;

    private static string? Overdue(IReadOnlyDictionary<string, string> facts) =>
        Number(facts, DaysOverdueKey) is var days && days > 0
            ? $"{days} day{(days == 1 ? string.Empty : "s")} overdue"
            : null;

    private static string QuestName(IReadOnlyDictionary<string, string> facts) =>
        QuestCatalog.Find(Text(facts, QuestKey))?.Name ?? Text(facts, QuestKey) ?? "a quest";

    private static string DungeonName(IReadOnlyDictionary<string, string> facts) =>
        DungeonCatalog.Find(Text(facts, DungeonKey))?.Name ?? Text(facts, DungeonKey) ?? "the deep";

    private static string? BannerName(IReadOnlyDictionary<string, string> facts) =>
        FactionCatalog.Find(Text(facts, FactionKey))?.Name;

    private static string Wins(IReadOnlyDictionary<string, string> facts) =>
        Number(facts, WinsKey) is var wins && wins == 1 ? "one won contract" : $"{wins} won contracts";

    /// <summary>"a Bog Lurker", "an Ogre". Crude, and right for every name in the catalogs.</summary>
    private static string Article(string noun) =>
        noun.Length > 0 && "AEIOU".Contains(char.ToUpperInvariant(noun[0]))
            ? $"an {noun}"
            : $"a {noun}";

    private static string? Text(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int Number(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : 0;

    private static string? Join(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        return kept.Length == 0 ? null : string.Join(" · ", kept);
    }
}
