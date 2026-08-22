namespace TodoApp.Models.Rpg;

/// <summary>
/// What to call a monster key, whichever catalog it came from.
/// </summary>
/// <remarks>
/// The <c>MonsterKey</c> column carries two key spaces, deliberately disjoint: bestiary keys from
/// a tavern or dungeon fight, and <see cref="HuntArchetypeCatalog"/> keys from a contract.
/// <see cref="MonsterCatalog.Find"/> is a plain dictionary lookup and returns null for the second
/// kind, so a caller that consulted it alone reported nothing at all once contracts outnumbered
/// any single bestiary monster, which is the normal steady state: one archetype key covers every
/// task of that shape. Falling through to the archetype, and then to the raw key, is what the
/// chronicle summary, the chronicle narration and the encounter mapper all need, so it is one
/// function rather than three copies.
/// </remarks>
public static class MonsterNames
{
    public static string Of(string monsterKey) =>
        MonsterCatalog.Find(monsterKey)?.Name
        ?? HuntArchetypeCatalog.Find(monsterKey)?.Noun
        ?? monsterKey;
}
