namespace TodoApp.Models.Rpg;

/// <summary>
/// One offer taken off one day's shelf. The shelf itself is still computed rather than stored
/// (DEC-002); this records the historical fact that gold changed hands, which nothing else in
/// the schema remembers.
/// </summary>
/// <remarks>
/// Without a row here the shelf refills on every request: stock is a pure function of the user
/// and the date, so the same offer id could be posted for as long as the gold held out, and
/// salvage turns each copy into essence. The daily cap the economy is balanced around is
/// exactly this table.
/// <para>
/// <see cref="OfferId"/> carries the date it was rolled for, so a row is dead the moment the
/// shelf rotates and old rows need no sweeping to stay correct.
/// </para>
/// </remarks>
public class ShopPurchase
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    /// <summary>The offer id as <c>ShopService.StockFor</c> composed it: date, slot, item key.</summary>
    public string OfferId { get; set; } = string.Empty;

    public DateTimeOffset PurchasedAt { get; set; } = DateTimeOffset.UtcNow;
}
