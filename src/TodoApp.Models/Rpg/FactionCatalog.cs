namespace TodoApp.Models.Rpg;

/// <summary>
/// How well a faction knows a hunter. Derived from won contracts, never stored.
/// </summary>
/// <remarks>
/// A record of fights that happened rather than a balance to spend, which is why there is
/// nothing here to inflate, drift or exploit. <c>DungeonService.DepthAsync</c> counts won
/// encounters for the same reason: a stored counter can disagree with the fights on the table,
/// a derived one cannot.
/// </remarks>
public enum FactionStanding
{
    Unknown = 0,
    Noticed = 1,
    Trusted = 2,
    Respected = 3,
    Sworn = 4
}

/// <summary>
/// A banner a contract can be flown under, matched from a task's own tags.
/// </summary>
/// <remarks>
/// Code-held per DEC-004. Only the key is ever persisted, and it is persisted frozen on the
/// encounter: a faction re-derived on read would let a player retag a task one keystroke before
/// the killing blow and redirect the reward to whichever banner is holding the item they want.
/// </remarks>
/// <param name="Aliases">
/// Tag names that muster under this banner, matched case-insensitively and never in SQL. The
/// existing tag filter is byte-exact Postgres array containment, so "work" does not find a task
/// tagged "Work"; faction resolution runs in C# on a materialised task instead, against an
/// OrdinalIgnoreCase index.
/// </param>
/// <param name="Titles">
/// What the contract board calls a hunter of each standing, indexed by
/// <see cref="FactionStanding"/>. Cosmetic, and deliberately the only thing standing changes
/// about the board itself.
/// </param>
/// <param name="RewardTable">
/// The faction's own table, passed to LootService.RollRewardAsync's existing table parameter.
/// Its own, never an existing monster's, and never a reweighting of one: PickWeighted rolls once
/// against the summed weight and walks in declaration order, so extending a table a seeded test
/// can reach hands that test a different item with no roll-count change to make the break
/// visible.
/// </param>
public sealed record FactionDefinition(
    string Key,
    string Name,
    string Blurb,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Titles,
    IReadOnlyList<LootEntry> RewardTable)
{
    /// <summary>The board's name for a hunter of this standing.</summary>
    /// <remarks>
    /// Bounds-checked rather than indexed blind, so adding a standing tier without adding its
    /// title is a plain label rather than an exception thrown on the contract board.
    /// </remarks>
    public string TitleAt(FactionStanding standing) =>
        (int)standing >= 0 && (int)standing < Titles.Count ? Titles[(int)standing] : Name;
}

/// <summary>
/// The standing ladder, shared by every faction. Code-held per DEC-004.
/// </summary>
/// <remarks>
/// Standing counts won hunts, not contracts taken, so a hunt fled is not standing. What it buys
/// is deliberately narrow, and what it must never buy is narrower still: not XP (DEC-012), not
/// stamina (DEC-003), not a completion, not the bounty multiplier, and not the stat block.
/// <para>
/// The stat block exclusion is the subtle one. Standing rises monotonically and is not a frozen
/// input, so a tier gained mid-fight would move MaxHitPoints and re-fire or skip a phase, which
/// is exactly the drift DEC-002 exists to prevent.
/// </para>
/// </remarks>
public static class FactionStandings
{
    /// <summary>The standing a hunter of this many won contracts holds with a faction.</summary>
    public static FactionStanding TierFor(int wonHunts) => wonHunts switch
    {
        <= 0 => FactionStanding.Unknown,
        < 5 => FactionStanding.Noticed,
        < 15 => FactionStanding.Trusted,
        < 40 => FactionStanding.Respected,
        _ => FactionStanding.Sworn
    };

    /// <summary>
    /// The worst a contract reward from this faction is allowed to be.
    /// </summary>
    /// <remarks>
    /// Passed as RollRewardAsync's existing floor parameter, so it costs no die and cannot cap a
    /// lucky roll: the floor lifts a poor result and never lowers a good one. Sworn stops at Rare
    /// rather than climbing to Epic because a floor above Rare would make the rarity roll itself
    /// almost irrelevant, and a reward nobody rolls for is a stipend with extra steps.
    /// </remarks>
    public static Rarity FloorFor(FactionStanding standing) => standing switch
    {
        FactionStanding.Trusted => Rarity.Uncommon,
        FactionStanding.Respected => Rarity.Rare,
        FactionStanding.Sworn => Rarity.Rare,
        _ => Rarity.Common
    };
}

/// <summary>Code-held, following DEC-004. Only the key is ever persisted.</summary>
public static class FactionCatalog
{
    public const string TheHearth = "the-hearth";
    public const string TheLedger = "the-ledger";
    public const string TheVigil = "the-vigil";
    public const string TheAthenaeum = "the-athenaeum";
    public const string TheExchequer = "the-exchequer";

    public static IReadOnlyList<FactionDefinition> All { get; } =
    [
        new(TheHearth, "The Hearth",
            "Keeps the fire in and the weather out. Notices when the floor has not been swept.",
            ["home", "house", "chores", "cleaning"],
            ["Stranger at the Door", "Expected", "Regular", "Of the House", "Hearthkeeper"],
            [
                new LootEntry(ItemCatalog.HeartwoodToken, 3),
                new LootEntry(ItemCatalog.OxhideBelt, 3),
                new LootEntry(ItemCatalog.PilgrimsCudgel, 2),
                new LootEntry(ItemCatalog.WayfarersCoat, 2),
                new LootEntry(ItemCatalog.AcolytesVestment, 2),
                new LootEntry(ItemCatalog.HermitsBell, 1)
            ]),

        new(TheLedger, "The Ledger",
            "Everything you did today, in a column, in a hand that is not yours.",
            ["work", "office", "job", "admin"],
            ["Unentered", "Noted", "In Good Standing", "Signatory", "Chief Clerk"],
            [
                new LootEntry(ItemCatalog.GuildSignet, 3),
                new LootEntry(ItemCatalog.Brigandine, 3),
                new LootEntry(ItemCatalog.OratorsCane, 2),
                new LootEntry(ItemCatalog.HeraldsBaton, 2),
                new LootEntry(ItemCatalog.CartographersLens, 2),
                new LootEntry(ItemCatalog.EnvoysTorc, 1)
            ]),

        new(TheVigil, "The Vigil",
            "Turns out at dawn whether or not you do. Has kept a place for you.",
            ["health", "fitness", "gym", "exercise"],
            ["Unenrolled", "Marked Present", "On the Roster", "Watch Sergeant", "Sworn Vigilant"],
            [
                new LootEntry(ItemCatalog.HideHarness, 3),
                new LootEntry(ItemCatalog.BoarSpear, 3),
                new LootEntry(ItemCatalog.QuarrymansGauntlets, 2),
                new LootEntry(ItemCatalog.RingOfVigour, 2),
                new LootEntry(ItemCatalog.PendantOfTheBear, 2),
                new LootEntry(ItemCatalog.IronFlail, 1)
            ]),

        new(TheAthenaeum, "The Athenaeum",
            "A room of books nobody has finished. Yours is on the third shelf.",
            ["study", "learning", "reading", "research"],
            ["Unread", "Enrolled", "Reader", "Fellow", "Keeper of the Stacks"],
            [
                new LootEntry(ItemCatalog.PhilosophersInkstone, 3),
                new LootEntry(ItemCatalog.ArcanistsWeave, 3),
                new LootEntry(ItemCatalog.RunedWand, 2),
                new LootEntry(ItemCatalog.OrreryStaff, 2),
                new LootEntry(ItemCatalog.AmuletOfInsight, 2),
                new LootEntry(ItemCatalog.CircletOfClarity, 1)
            ]),

        new(TheExchequer, "The Exchequer",
            "Counts what is owed, patiently, and has never once lost a figure.",
            ["finance", "money", "tax", "bills"],
            ["Unaccounted", "On the Books", "In Credit", "Bonded", "Master of Coin"],
            [
                new LootEntry(ItemCatalog.IronBand, 3),
                new LootEntry(ItemCatalog.DuellingRapier, 3),
                new LootEntry(ItemCatalog.TumblersSash, 2),
                new LootEntry(ItemCatalog.ShadowweaveCloak, 2),
                new LootEntry(ItemCatalog.RingOfFocus, 2),
                new LootEntry(ItemCatalog.LuckyCoin, 1)
            ])
    ];

    private static readonly Dictionary<string, FactionDefinition> ByKey =
        All.ToDictionary(f => f.Key, StringComparer.Ordinal);

    /// <summary>
    /// Every alias in the catalog, pointing at its faction.
    /// </summary>
    /// <remarks>
    /// OrdinalIgnoreCase because casing genuinely varies in the data. NormalizeTags preserves the
    /// case the user typed and dedupes case-insensitively within one task only, and
    /// GET /api/tasks/tags dedupes across tasks by whichever row Postgres returned first, so
    /// "Work", "work" and "WORK" all exist and all mean the Ledger.
    /// <para>
    /// A duplicate alias across two factions throws here, at type initialisation, and that is the
    /// intent: a tag that musters under two banners has no defined winner, and finding that out
    /// on the first build is better than finding it out from whichever faction happened to be
    /// declared second.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, FactionDefinition> ByAlias =
        All.SelectMany(f => f.Aliases.Select(a => (Alias: a, Faction: f)))
            .ToDictionary(x => x.Alias, x => x.Faction, StringComparer.OrdinalIgnoreCase);

    public static FactionDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var found) ? found : null;

    public static bool Exists(string? key) => key is not null && ByKey.ContainsKey(key);

    /// <summary>
    /// The faction a single tag musters under, or null.
    /// </summary>
    /// <remarks>
    /// Trims defensively rather than trusting the stored value. NormalizeTags only runs on the
    /// create and update endpoints, so a tag written by a seed, a migration or directly on the
    /// model never passed through it and can carry whitespace.
    /// </remarks>
    public static FactionDefinition? FindByTag(string? tag) =>
        tag is not null && ByAlias.TryGetValue(tag.Trim(), out var found) ? found : null;

    /// <summary>
    /// Which banner a task's contract flies under, as a catalog key, or null for none.
    /// </summary>
    /// <remarks>
    /// Tag order is insertion order and NormalizeTags preserves it, so the first tag that names a
    /// faction is the primary one. Walked rather than indexed, so a task tagged
    /// ["urgent", "Work"] still musters under the Ledger.
    /// <para>
    /// Returns the catalog key and never the tag string, which is what keeps a user's casing out
    /// of the database entirely: whatever "WORK" looked like when it was typed, what is stored is
    /// "the-ledger".
    /// </para>
    /// </remarks>
    public static string? FactionFor(TodoTask task) =>
        task.Tags.Select(FindByTag).FirstOrDefault(f => f is not null)?.Key;
}
