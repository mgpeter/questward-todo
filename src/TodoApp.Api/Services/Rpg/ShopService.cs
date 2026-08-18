using Microsoft.EntityFrameworkCore;
using Npgsql;
using TodoApp.Data;
using TodoApp.Models.Dice;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

/// <param name="OfferId">Stable for the day, so the client can post it back to buy.</param>
public sealed record ShopOffer(string OfferId, ItemDefinition Item, Rarity Rarity, int Price);

public sealed record ShopStock(IReadOnlyList<ShopOffer> Offers, DateTimeOffset RotatesAt);

public sealed record PurchaseResult(InventoryItem Item, int GoldSpent, int Gold);

public sealed record UpgradeResult(InventoryItem Item, Rarity From, Rarity To, int GoldSpent, int Gold);

/// <summary>
/// The shop. Stock is computed from the user and the date rather than stored, so there is
/// no stock table and no nightly job, and the shelves are identical all day.
/// </summary>
/// <remarks>
/// Stock is deliberately capped at Rare. Gold is plentiful once fights are going, and a
/// shop selling Legendary gear would make loot drops pointless. The best items still have
/// to be won, or upgraded into.
/// </remarks>
public sealed class ShopService(TodoDbContext db)
{
    public const int OfferCount = 6;

    /// <summary>Nothing above this appears on the shelf.</summary>
    public const Rarity MaxStockRarity = Rarity.Rare;

    private static readonly (int UpTo, Rarity Rarity)[] StockRarityTable =
    [
        (60, Rarity.Common),
        (90, Rarity.Uncommon),
        (100, Rarity.Rare)
    ];

    public static ShopStock StockFor(Guid userId, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var roller = new SeededDiceRoller(SeededDiceRoller.DailySeed(userId, today));

        var catalogue = ItemCatalog.All;
        var offers = new List<ShopOffer>(OfferCount);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        // Sampling without replacement, so one shelf never shows the same item twice.
        for (var slot = 0; slot < OfferCount && taken.Count < catalogue.Count; slot++)
        {
            ItemDefinition item;

            do
            {
                item = catalogue[roller.Roll(catalogue.Count) - 1];
            }
            while (!taken.Add(item.Key));

            var rarity = RollStockRarity(roller);

            offers.Add(new ShopOffer(
                OfferId: $"{today:yyyyMMdd}-{slot}-{item.Key}",
                Item: item,
                Rarity: rarity,
                Price: item.ValueAt(rarity)));
        }

        var rotatesAt = new DateTimeOffset(today.AddDays(1), TimeOnly.MinValue, TimeSpan.Zero);

        return new ShopStock(offers, rotatesAt);
    }

    /// <summary>
    /// Which of today's offers this user has already taken off the shelf. Matched on the date
    /// the offer id carries rather than on PurchasedAt, so it answers the same question the
    /// shelf was computed from even for a request that spans midnight.
    /// </summary>
    public async Task<IReadOnlyCollection<string>> SoldOutAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var today = $"{DateOnly.FromDateTime(now.UtcDateTime):yyyyMMdd}-";

        var sold = await db.ShopPurchases
            .Where(p => p.UserId == userId && p.OfferId.StartsWith(today))
            .Select(p => p.OfferId)
            .ToListAsync(cancellationToken);

        return sold.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Sells one offer once. The purchase row is the whole point: the shelf is a pure function
    /// of the user and the date, so without a record of what was bought the same offer id is
    /// buyable for as long as the gold lasts, and the forge turns each copy into essence.
    /// </summary>
    public async Task<RpgResult<PurchaseResult>> BuyAsync(
        Guid userId,
        string offerId,
        CancellationToken cancellationToken)
    {
        // Recomputed server-side rather than trusted from the request. Without this an
        // offer id could be forged to buy a Legendary at Common prices.
        var stock = StockFor(userId, DateTimeOffset.UtcNow);
        var offer = stock.Offers.FirstOrDefault(o => o.OfferId == offerId);

        if (offer is null)
        {
            return RpgResult<PurchaseResult>.Fail(
                RpgFailure.NotFound, "That is not on the shelf today.");
        }

        // Answered here so the ordinary second click reads as a sold-out shelf rather than as
        // a database error. The unique index on (UserId, OfferId) is what enforces it.
        var alreadyBought = await db.ShopPurchases
            .AnyAsync(p => p.UserId == userId && p.OfferId == offerId, cancellationToken);

        if (alreadyBought)
        {
            return SoldOut(offer);
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);

        if (character.Gold < offer.Price)
        {
            return RpgResult<PurchaseResult>.Fail(
                RpgFailure.NotEnoughGold,
                $"{offer.Item.Name} costs {offer.Price} gold and you have {character.Gold}.");
        }

        character.Gold -= offer.Price;

        var item = new InventoryItem
        {
            UserId = userId,
            ItemKey = offer.Item.Key,
            Slot = offer.Item.Slot,
            Rarity = offer.Rarity,
            IsEquipped = false,
            AcquiredAt = DateTimeOffset.UtcNow
        };

        db.InventoryItems.Add(item);

        db.ShopPurchases.Add(new ShopPurchase
        {
            UserId = userId,
            OfferId = offerId,
            PurchasedAt = DateTimeOffset.UtcNow
        });

        try
        {
            // Note what is absent: nothing here touches TotalXp. Gold buys gear, never levels.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicatePurchase(exception))
        {
            // Two clicks that both got past the check above. The gold deduction and the item
            // went down in the same transaction as the purchase row, so the loser of the race
            // bought nothing and paid nothing.
            db.ChangeTracker.Clear();

            return SoldOut(offer);
        }

        return RpgResult<PurchaseResult>.Success(new PurchaseResult(item, offer.Price, character.Gold));
    }

    /// <summary>Cost to raise an item to the given rarity.</summary>
    public static int UpgradeCost(ItemDefinition item, Rarity target) =>
        Math.Max(25, item.ValueAt(target) - item.ValueAt(target - 1));

    public async Task<RpgResult<UpgradeResult>> UpgradeAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, cancellationToken);

        if (item is null)
        {
            return RpgResult<UpgradeResult>.Fail(RpgFailure.NotFound, "No such item.");
        }

        if (item.Rarity >= Rarity.Legendary)
        {
            return RpgResult<UpgradeResult>.Fail(
                RpgFailure.CannotUpgrade, "Legendary is as far as it goes.");
        }

        var definition = ItemCatalog.Find(item.ItemKey);

        if (definition is null)
        {
            return RpgResult<UpgradeResult>.Fail(
                RpgFailure.CannotUpgrade, "That item no longer exists in the catalogue.");
        }

        var target = item.Rarity + 1;
        var cost = UpgradeCost(definition, target);

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);

        if (character.Gold < cost)
        {
            return RpgResult<UpgradeResult>.Fail(
                RpgFailure.NotEnoughGold,
                $"Reforging to {RarityRules.Describe(target)} costs {cost} gold and you have {character.Gold}.");
        }

        var from = item.Rarity;

        character.Gold -= cost;
        item.Rarity = target;

        // Equipped items can be upgraded in place; making people unequip first would be
        // friction for no benefit.
        await db.SaveChangesAsync(cancellationToken);

        return RpgResult<UpgradeResult>.Success(
            new UpgradeResult(item, from, target, cost, character.Gold));
    }

    private static RpgResult<PurchaseResult> SoldOut(ShopOffer offer) =>
        RpgResult<PurchaseResult>.Fail(
            RpgFailure.OfferSoldOut,
            $"You have already taken the {offer.Item.Name} off today's shelf. It restocks tomorrow.");

    /// <summary>
    /// The unique index on (UserId, OfferId), and only that one. Any other constraint failure
    /// is a bug and must keep surfacing as one rather than reading to the player as a sold-out
    /// shelf.
    /// </summary>
    private static bool IsDuplicatePurchase(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        && exception.Entries.Any(e => e.Entity is ShopPurchase);

    private static Rarity RollStockRarity(IDiceRoller roller)
    {
        var roll = roller.Roll(100);

        foreach (var (upTo, rarity) in StockRarityTable)
        {
            if (roll <= upTo)
            {
                return rarity;
            }
        }

        return Rarity.Common;
    }
}
