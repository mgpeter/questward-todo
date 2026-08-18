namespace TodoApp.Models.Rpg;

/// <param name="Pieces">How many of the set must be equipped before this tier pays.</param>
public sealed record SetBonus(int Pieces, string Description, BonusEffects Effect);

/// <summary>
/// A set is exactly one weapon, one armour and one trinket. There are exactly three equip
/// slots, so any other composition would be uncompletable, which is what makes two and three
/// the only meaningful thresholds.
/// </summary>
public sealed record SetDefinition(
    string Key,
    string Name,
    string Blurb,
    IReadOnlyList<string> ItemKeys,
    IReadOnlyList<SetBonus> Bonuses)
{
    public int Total => ItemKeys.Count;

    public bool Contains(string? itemKey) =>
        itemKey is not null && ItemKeys.Contains(itemKey, StringComparer.Ordinal);

    /// <summary>Cumulative: three pieces pays the two-piece tier as well as the three.</summary>
    public IReadOnlyList<SetBonus> ActiveAt(int equipped) =>
        [.. Bonuses.Where(b => equipped >= b.Pieces)];
}

public sealed record SetProgress(SetDefinition Set, int Equipped, IReadOnlyList<SetBonus> Active);

/// <summary>
/// Code-held, following DEC-004, and this one persists nothing at all: membership is a pure
/// function of the item key, and completion a pure function of the equipped rows.
/// </summary>
/// <remarks>
/// No <c>SetKey</c> column on the item and no <c>ActiveSetKey</c> on the character, per
/// DEC-002. Both are derivable, and a cached copy would go stale the moment a set is
/// recomposed here, which is exactly the change DEC-004 exists to make free.
/// <para>
/// Set bonuses are flat and never scale with rarity. Rarity already scales the base item and
/// the affix tier; a third multiplier on the same score is where the numbers run away.
/// </para>
/// </remarks>
public static class SetCatalog
{
    public const string Valewarden = "valewarden";
    public const string NightfallVigil = "nightfall-vigil";
    public const string WanderingScholar = "wandering-scholar";
    public const string BearsDue = "bears-due";
    public const string DawnwardOath = "dawnward-oath";

    private static BonusEffects Armour(int bonus) => new(AbilityScores.Zero, bonus, 0, 0, 0);

    private static BonusEffects Attack(int bonus) => new(AbilityScores.Zero, 0, bonus, 0, 0);

    private static BonusEffects Damage(int bonus) => new(AbilityScores.Zero, 0, 0, bonus, 0);

    private static BonusEffects Critical(int bonus) => new(AbilityScores.Zero, 0, 0, 0, bonus);

    private static BonusEffects Score(Ability ability, int bonus) =>
        new(AbilityScores.Zero.Plus(ability, bonus), 0, 0, 0, 0);

    public static IReadOnlyList<SetDefinition> All { get; } =
    [
        new(Valewarden, "Valewarden",
            "Green-dyed kit, worn by people who watch a border for a living.",
            [ItemCatalog.LongbowOfTheVale, ItemCatalog.LeatherArmour, ItemCatalog.BootsOfSpeed],
            [
                new(2, "+1 armour class", Armour(1)),
                new(3, "+1 to attack rolls", Attack(1))
            ]),

        new(NightfallVigil, "The Nightfall Vigil",
            "Everything about it is quiet, including the people who wore it first.",
            [ItemCatalog.SilveredBlade, ItemCatalog.ShadowweaveCloak, ItemCatalog.GlovesOfTheThief],
            [
                new(2, "+1 Dexterity", Score(Ability.Dexterity, 1)),

                // Safe as a weapon-bound lever precisely because the third piece is the set's
                // own weapon, so there is always something to sharpen.
                new(3, "critical range improves by 1", Critical(1))
            ]),

        new(WanderingScholar, "The Wandering Scholar",
            "Chalk dust in the seams, and a habit of reading while walking.",
            [ItemCatalog.OakenStaff, ItemCatalog.TravellersRobes, ItemCatalog.CircletOfClarity],
            [
                new(2, "+1 Intelligence", Score(Ability.Intelligence, 1)),
                new(3, "+2 Intelligence", Score(Ability.Intelligence, 2))
            ]),

        new(BearsDue, "The Bear's Due",
            "Heavy, unsubtle, and paid for in advance.",
            [ItemCatalog.GreatAxe, ItemCatalog.ScaleMail, ItemCatalog.PendantOfTheBear],
            [
                new(2, "+1 Strength", Score(Ability.Strength, 1)),
                new(3, "+1 weapon damage", Damage(1))
            ]),

        new(DawnwardOath, "Dawnward Oath",
            "Sworn at first light, by people who meant it.",
            [ItemCatalog.ReliquaryHammer, ItemCatalog.BreastplateOfDawn, ItemCatalog.RingOfFocus],
            [
                new(2, "+1 armour class", Armour(1)),
                new(3, "+1 Wisdom and +1 armour class", Score(Ability.Wisdom, 1).Plus(Armour(1)))
            ])
    ];

    /// <summary>
    /// One item belongs to at most one set. Built with <c>ToDictionary</c> on purpose: a key
    /// listed in two sets throws here, at startup, rather than making a piece count twice.
    /// </summary>
    private static readonly Dictionary<string, SetDefinition> BySetItem =
        All.SelectMany(s => s.ItemKeys.Select(k => (Key: k, Set: s)))
            .ToDictionary(p => p.Key, p => p.Set, StringComparer.Ordinal);

    public static SetDefinition? ForItem(string? itemKey) =>
        itemKey is not null && BySetItem.TryGetValue(itemKey, out var found) ? found : null;

    public static SetDefinition? Find(string? key) =>
        key is null ? null : All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Progress for every set the wearer has at least one piece of, counted by distinct item
    /// key among the equipped rows only.
    /// </summary>
    /// <remarks>
    /// Bag contents are not a wearing decision, so completion flows through the equipped list
    /// the sheet already has, with no extra query. Distinctness is guaranteed in the database
    /// by the partial unique index, but this is a pure static that tests hand arbitrary lists
    /// to, so it counts distinct anyway.
    /// </remarks>
    public static IReadOnlyList<SetProgress> ProgressFor(IReadOnlyList<InventoryItem> equipped)
    {
        var keys = equipped
            .Where(i => i.IsEquipped)
            .Select(i => i.ItemKey)
            .ToHashSet(StringComparer.Ordinal);

        List<SetProgress> progress = [];

        foreach (var set in All)
        {
            var pieces = set.ItemKeys.Count(keys.Contains);

            if (pieces > 0)
            {
                progress.Add(new SetProgress(set, pieces, set.ActiveAt(pieces)));
            }
        }

        return progress;
    }

    public static BonusEffects BonusesFor(IReadOnlyList<InventoryItem> equipped)
    {
        var total = BonusEffects.None;

        foreach (var progress in ProgressFor(equipped))
        {
            foreach (var bonus in progress.Active)
            {
                total = total.Plus(bonus.Effect);
            }
        }

        return total;
    }
}
