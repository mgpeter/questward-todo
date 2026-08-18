using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

public sealed record SalvageResult(int EssenceGained, int Essence);

public sealed record CraftResult(InventoryItem Item, int EssenceSpent, int Essence);

/// <summary>
/// Breaking items down for essence, and spending essence to put words on the ones you kept.
/// </summary>
/// <remarks>
/// Salvage needs no dice and crafting does, but all three verbs live together because they are
/// the only things in the app that move essence: retuning the economy is one file to read
/// rather than three to find. Deliberately outside the combat service graph, so no existing
/// harness changes shape.
/// <para>
/// Nothing here touches <c>TotalXp</c> (DEC-012). Essence buys magnitude, never progression.
/// </para>
/// </remarks>
public sealed class ForgeService(TodoDbContext db, IDiceRoller roller)
{
    /// <summary>
    /// Destroys the item and pays essence, never gold. Selling pays gold, never essence: one
    /// item, one choice, and that choice is the whole point of the material existing.
    /// </summary>
    public async Task<RpgResult<SalvageResult>> SalvageAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await FindAsync(userId, itemId, cancellationToken);

        if (item is null)
        {
            return RpgResult<SalvageResult>.Fail(RpgFailure.NotFound, "No such item.");
        }

        if (item.IsEquipped)
        {
            return RpgResult<SalvageResult>.Fail(
                RpgFailure.ItemEquipped, "Take it off before breaking it down.");
        }

        var essence = ForgeRules.EssenceFor(item);

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        character.Essence += essence;

        db.InventoryItems.Remove(item);

        await db.SaveChangesAsync(cancellationToken);

        return RpgResult<SalvageResult>.Success(new SalvageResult(essence, character.Essence));
    }

    /// <summary>
    /// Fills one empty affix slot by rolling into it.
    /// </summary>
    /// <remarks>
    /// The roll runs through <see cref="AffixRules"/> rather than picking a word here, so
    /// <c>MinimumRarity</c> is honoured exactly as it is on a drop and essence can never buy a
    /// Rare-only word onto an Uncommon item.
    /// </remarks>
    public async Task<RpgResult<CraftResult>> ImbueAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await FindAsync(userId, itemId, cancellationToken);

        if (item is null)
        {
            return RpgResult<CraftResult>.Fail(RpgFailure.NotFound, "No such item.");
        }

        if (Retired(item))
        {
            return Gone<CraftResult>();
        }

        var (prefix, suffix) = AffixRules.InForce(item);
        var free = AffixRules.RollableFor(item.Slot, item.Rarity) - AffixRules.CountInForce(item);

        // A Common item and a consumable both land here, because both roll zero slots. One arm
        // rather than a special case for each is the RollableFor ruling paying for itself.
        if (free <= 0)
        {
            return RpgResult<CraftResult>.Fail(
                RpgFailure.CannotUpgrade, "There is no room on it for another word.");
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var cost = ForgeRules.ImbueCost(item.Rarity);

        // Checked before the roll, not after. A refused craft that had already spent a die
        // would make the outcome of the next paid one depend on how many times you failed.
        if (character.Essence < cost)
        {
            return Broke<CraftResult>(cost, character.Essence);
        }

        // Whichever slot is empty. Both empty means an item that was upgraded into its second
        // slot rather than dropped with one, and that rolls over the combined pool exactly as
        // a fresh drop does.
        var kind = (prefix, suffix) switch
        {
            (null, null) => (AffixKind?)null,
            (null, _) => AffixKind.Prefix,
            _ => AffixKind.Suffix
        };

        var rolled = AffixRules.RollOne(item.Slot, item.Rarity, kind, roller);

        if (rolled is null)
        {
            return RpgResult<CraftResult>.Fail(
                RpgFailure.CannotUpgrade, "There is no word that would sit on it.");
        }

        Write(item, rolled);

        character.Essence -= cost;

        await db.SaveChangesAsync(cancellationToken);

        return RpgResult<CraftResult>.Success(new CraftResult(item, cost, character.Essence));
    }

    /// <summary>
    /// Rerolls every affix currently in force, each excluding the word it is replacing.
    /// </summary>
    /// <remarks>
    /// The exclusion is the feature: paying twice the imbue price to be handed back the word
    /// you were paying to be rid of would read as the forge having taken the essence and done
    /// nothing. An empty slot is left empty, because filling it is what imbue is for.
    /// </remarks>
    public async Task<RpgResult<CraftResult>> ReforgeAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await FindAsync(userId, itemId, cancellationToken);

        if (item is null)
        {
            return RpgResult<CraftResult>.Fail(RpgFailure.NotFound, "No such item.");
        }

        if (Retired(item))
        {
            return Gone<CraftResult>();
        }

        var (prefix, suffix) = AffixRules.InForce(item);

        if (prefix is null && suffix is null)
        {
            return RpgResult<CraftResult>.Fail(
                RpgFailure.CannotUpgrade, "There is nothing on it to reforge.");
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var cost = ForgeRules.ReforgeCost(item.Rarity);

        if (character.Essence < cost)
        {
            return Broke<CraftResult>(cost, character.Essence);
        }

        var newPrefix = prefix is null
            ? null
            : AffixRules.RollOne(item.Slot, item.Rarity, AffixKind.Prefix, roller, excluding: prefix.Key);

        var newSuffix = suffix is null
            ? null
            : AffixRules.RollOne(item.Slot, item.Rarity, AffixKind.Suffix, roller, excluding: suffix.Key);

        // Only reachable if a kind's eligible pool ever narrows to the single word already in
        // the slot. Refusing costs nothing; charging for a reroll that cannot change anything
        // would be the forge taking payment for standing still.
        if ((prefix is not null && newPrefix is null) || (suffix is not null && newSuffix is null))
        {
            return RpgResult<CraftResult>.Fail(
                RpgFailure.CannotUpgrade, "There is nothing else it could become.");
        }

        if (newPrefix is not null)
        {
            Write(item, newPrefix);
        }

        if (newSuffix is not null)
        {
            Write(item, newSuffix);
        }

        character.Essence -= cost;

        await db.SaveChangesAsync(cancellationToken);

        return RpgResult<CraftResult>.Success(new CraftResult(item, cost, character.Essence));
    }

    /// <summary>
    /// Scoped to the owner in the query itself, so another user's id is indistinguishable from
    /// one that never existed and item ids cannot be probed for.
    /// </summary>
    private Task<InventoryItem?> FindAsync(Guid userId, Guid itemId, CancellationToken cancellationToken) =>
        db.InventoryItems.FirstOrDefaultAsync(
            i => i.Id == itemId && i.UserId == userId, cancellationToken);

    /// <summary>
    /// A row whose key has left the catalog (DEC-004). Salvage still pays it the floor, because
    /// refusing would strand the row forever; crafting must refuse, because the sheet skips a
    /// retired definition before it ever reads the affix, so the word would be paid for and
    /// then do nothing. The upgrade bench already makes this ruling before spending gold.
    /// </summary>
    private static bool Retired(InventoryItem item) => ItemCatalog.Find(item.ItemKey) is null;

    private static RpgResult<T> Gone<T>() =>
        RpgResult<T>.Fail(RpgFailure.CannotUpgrade, "That item no longer exists in the catalogue.");

    private static void Write(InventoryItem item, AffixDefinition affix)
    {
        if (affix.Kind == AffixKind.Prefix)
        {
            item.PrefixKey = affix.Key;
        }
        else
        {
            item.SuffixKey = affix.Key;
        }
    }

    private static RpgResult<T> Broke<T>(int cost, int held) =>
        RpgResult<T>.Fail(
            RpgFailure.NotEnoughEssence,
            $"The forge wants {cost} essence and you have {held}.");
}
