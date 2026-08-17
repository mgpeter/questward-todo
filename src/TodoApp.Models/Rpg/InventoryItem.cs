namespace TodoApp.Models.Rpg;

/// <summary>
/// One acquired item. <see cref="Rarity"/> is stored because it was rolled for this
/// particular drop; <see cref="ItemKey"/> points at the code-held catalog (DEC-004).
/// </summary>
public class InventoryItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string ItemKey { get; set; } = string.Empty;

    public Rarity Rarity { get; set; }

    /// <summary>
    /// Denormalised from the catalog so the partial unique index enforcing one equipped
    /// item per slot can exist at all.
    /// </summary>
    public ItemSlot Slot { get; set; }

    public bool IsEquipped { get; set; }

    public DateTimeOffset AcquiredAt { get; set; } = DateTimeOffset.UtcNow;

    public ItemDefinition? Definition => ItemCatalog.Find(ItemKey);
}
