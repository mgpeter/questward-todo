using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

/// <summary>
/// The one place an item becomes a row, and the one place a unit of one is spent.
/// </summary>
/// <remarks>
/// Consumables are stored one row per user, key and rarity with a count, enforced by a partial
/// unique index. That makes every acquisition an upsert: a bare <c>Add</c> of a second Draught
/// of Mending at the same rarity is a constraint violation and a 500 rather than a second
/// potion. The shop and the loot service both acquire items and both used to add rows directly,
/// so they share this rather than each growing their own copy of the rule and drifting apart.
/// <para>
/// Spending has the mirror problem. Selling one potion out of six and salvaging one out of six
/// both used to remove the row, which would have destroyed the whole stack for one item's gold
/// or essence.
/// </para>
/// </remarks>
public static class InventoryStack
{
    /// <summary>
    /// Puts one of an item in the bag, stacking it when the slot stacks.
    /// </summary>
    /// <returns>
    /// The row the item landed on, which is a fresh one for anything worn and may be an
    /// existing stack for a consumable.
    /// </returns>
    public static async Task<InventoryItem> AcquireAsync(
        TodoDbContext db,
        Guid userId,
        ItemDefinition definition,
        Rarity rarity,
        CancellationToken cancellationToken)
    {
        if (definition.Slot == ItemSlot.Consumable)
        {
            var stack = await FindStackAsync(db, userId, definition.Key, rarity, cancellationToken);

            if (stack is not null)
            {
                stack.Quantity++;

                return stack;
            }
        }

        var item = new InventoryItem
        {
            UserId = userId,
            ItemKey = definition.Key,
            Slot = definition.Slot,
            Rarity = rarity,
            Quantity = 1,
            IsEquipped = false,
            AcquiredAt = DateTimeOffset.UtcNow
        };

        db.InventoryItems.Add(item);

        return item;
    }

    /// <summary>Spends one unit, removing the row once the last one goes.</summary>
    public static void ConsumeOne(TodoDbContext db, InventoryItem item)
    {
        if (item.Quantity > 1)
        {
            item.Quantity--;

            return;
        }

        db.InventoryItems.Remove(item);
    }

    /// <summary>
    /// The stack an acquisition should land on, or null when this is the first of its kind.
    /// </summary>
    /// <remarks>
    /// The change tracker is consulted before the database. A row added earlier in the same unit
    /// of work has not been written yet, so a query cannot see it, and two acquisitions in one
    /// SaveChanges would each add a row and lose to the index together. Rows already marked for
    /// deletion are skipped, because reviving one and inserting beside it are the same failure.
    /// </remarks>
    private static async Task<InventoryItem?> FindStackAsync(
        TodoDbContext db,
        Guid userId,
        string itemKey,
        Rarity rarity,
        CancellationToken cancellationToken)
    {
        var pending = db.ChangeTracker.Entries<InventoryItem>()
            .Where(e => e.State != EntityState.Deleted)
            .Select(e => e.Entity)
            .FirstOrDefault(i =>
                i.UserId == userId
                && i.Slot == ItemSlot.Consumable
                && i.Rarity == rarity
                && string.Equals(i.ItemKey, itemKey, StringComparison.Ordinal));

        return pending ?? await db.InventoryItems.FirstOrDefaultAsync(
            i => i.UserId == userId
                && i.Slot == ItemSlot.Consumable
                && i.Rarity == rarity
                && i.ItemKey == itemKey,
            cancellationToken);
    }
}
