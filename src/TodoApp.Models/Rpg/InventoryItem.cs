namespace TodoApp.Models.Rpg;

/// <summary>
/// One acquired item. <see cref="Rarity"/> is stored because it was rolled for this
/// particular drop; <see cref="ItemKey"/> points at the code-held catalog (DEC-004).
/// </summary>
/// <remarks>
/// Everything below <see cref="AcquiredAt"/> is computed on read from the stored keys and
/// rarity, and stays unmapped because a get-only property with no backing field is not
/// discovered by EF Core. <see cref="PrefixKey"/> and <see cref="SuffixKey"/> are the
/// opposite case: ordinary settable properties, so they are mapped by convention the moment
/// they exist and need their column type and migration in the same change, or the model will
/// expect columns the database does not have.
/// </remarks>
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

    /// <summary>
    /// The prefix rolled at drop time or paid for at the forge. Stored for the same reason
    /// <see cref="Rarity"/> is: it is the outcome of a die, and re-rolling it on read would
    /// hand the player a different item every request.
    /// </summary>
    public string? PrefixKey { get; set; }

    /// <inheritdoc cref="PrefixKey"/>
    public string? SuffixKey { get; set; }

    public DateTimeOffset AcquiredAt { get; set; } = DateTimeOffset.UtcNow;

    public ItemDefinition? Definition => ItemCatalog.Find(ItemKey);

    /// <summary>The set this piece belongs to, or null. Derived, never stored (DEC-002).</summary>
    public SetDefinition? Set => SetCatalog.ForItem(ItemKey);

    /// <summary>
    /// The name a player sees: prefix, catalog name, suffix, with the empty parts trimmed.
    /// Composed on read from three keys, so retuning or retiring an affix renames every item
    /// carrying it without touching a row.
    /// </summary>
    public string DisplayName => AffixRules.DisplayName(this);

    /// <summary>
    /// What the affixes in force contribute, at this item's rarity. Nothing at all once the key
    /// has left the catalog: the sheet skips a retired definition before it reads the affix, so
    /// an item card that still counted the word would advertise armour class and ability scores
    /// that wearing it does not grant.
    /// </summary>
    public BonusEffects AffixEffects =>
        Definition is null ? BonusEffects.None : AffixRules.EffectsOf(this);

    /// <summary>
    /// The item's ability bonuses including its affixes. Set bonuses are deliberately absent:
    /// they belong to the wearer's equipped combination, not to any one piece.
    /// </summary>
    public AbilityScores AbilityBonuses =>
        (Definition?.AbilityBonusesAt(Rarity) ?? AbilityScores.Zero).Plus(AffixEffects.Abilities);

    /// <summary>
    /// Armour class from the item plus its affixes. A Warded trinket contributes here too,
    /// which is why this is not gated on the slot being Armour.
    /// </summary>
    public int ArmourBonus => (Definition?.ArmourBonusAt(Rarity) ?? 0) + AffixEffects.ArmourBonus;
}
