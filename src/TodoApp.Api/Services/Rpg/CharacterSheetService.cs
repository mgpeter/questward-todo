using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

/// <summary>
/// Assembles the character sheet from the stored character and their equipped items, and
/// owns hit point recovery.
/// </summary>
public sealed class CharacterSheetService(TodoDbContext db)
{
    /// <summary>One hit point back per this much elapsed time, so resting is worth something.</summary>
    public static readonly TimeSpan RegenerationInterval = TimeSpan.FromMinutes(8);

    /// <summary>
    /// When the next point comes back, and when the character will be whole again.
    /// </summary>
    /// <remarks>
    /// Surfaced because the mechanic was invisible: hit points recovered silently and the
    /// app read as broken to anyone watching a bar that never moved.
    /// </remarks>
    public static (DateTimeOffset? NextPoint, DateTimeOffset? FullyHealed) RegenerationForecast(
        Character character,
        CharacterSheet sheet,
        DateTimeOffset now)
    {
        var missing = sheet.MaxHitPoints - character.CurrentHitPoints;

        if (missing <= 0)
        {
            return (null, null);
        }

        var anchor = character.HitPointsUpdatedAt ?? now;

        // Elapsed time already banked toward the next point carries over, so the countdown
        // does not silently restart on every read.
        var elapsed = now - anchor;
        var sinceLastPoint = TimeSpan.FromTicks(elapsed.Ticks % RegenerationInterval.Ticks);
        var next = now + (RegenerationInterval - sinceLastPoint);

        return (next, next + (RegenerationInterval * (missing - 1)));
    }

    public async Task<CharacterSheet> BuildAsync(Character character, CancellationToken cancellationToken)
    {
        var equipped = await db.InventoryItems
            .AsNoTracking()
            .Where(i => i.UserId == character.UserId && i.IsEquipped)
            .ToListAsync(cancellationToken);

        return Build(character, equipped);
    }

    public static CharacterSheet Build(Character character, IReadOnlyList<InventoryItem> equipped) =>
        CharacterSheet.Compute(
            ClassCatalog.Find(character.ClassKey),
            LevelCurve.LevelForXp(character.TotalXp),
            character.AbilityScores,
            EffectsOf(equipped));

    public static EquipmentEffects EffectsOf(IReadOnlyList<InventoryItem> equipped)
    {
        var bonuses = AbilityScores.Zero;
        var armour = 0;
        DiceExpressionHolder weapon = default;

        foreach (var item in equipped)
        {
            var definition = ItemCatalog.Find(item.ItemKey);

            // An item whose definition has been retired reads as nothing rather than
            // crashing the sheet, the same way a retired badge key does.
            if (definition is null)
            {
                continue;
            }

            bonuses = bonuses.Plus(definition.AbilityBonusesAt(item.Rarity));

            if (definition.Slot == ItemSlot.Armour)
            {
                armour += definition.ArmourBonusAt(item.Rarity);
            }

            if (definition.Slot == ItemSlot.Weapon && definition.Damage is { } damage)
            {
                weapon = new DiceExpressionHolder(damage, definition.Finesse, definition.BonusAbility);
            }
        }

        return new EquipmentEffects(
            bonuses,
            armour,
            weapon.Damage,
            weapon.Finesse,
            weapon.Ability ?? (weapon.Damage is null ? null : Ability.Strength));
    }

    /// <summary>
    /// Brings current hit points up to date before they are read or used.
    /// </summary>
    /// <remarks>
    /// A null timestamp means the character predates the RPG layer, so they start at full
    /// rather than dead. Regeneration is computed from elapsed time on read instead of a
    /// background job, which would be a lot of machinery for a personal app.
    /// </remarks>
    public static bool NormaliseHitPoints(Character character, CharacterSheet sheet, DateTimeOffset now)
    {
        if (character.HitPointsUpdatedAt is null)
        {
            character.CurrentHitPoints = sheet.MaxHitPoints;
            character.HitPointsUpdatedAt = now;
            return true;
        }

        if (character.CurrentHitPoints >= sheet.MaxHitPoints)
        {
            // Clamp downward too: losing Constitution to an unequip can leave current
            // above the new maximum.
            if (character.CurrentHitPoints > sheet.MaxHitPoints)
            {
                character.CurrentHitPoints = sheet.MaxHitPoints;
                character.HitPointsUpdatedAt = now;
                return true;
            }

            return false;
        }

        var intervals = (int)((now - character.HitPointsUpdatedAt.Value) / RegenerationInterval);

        if (intervals <= 0)
        {
            return false;
        }

        character.CurrentHitPoints = Math.Min(sheet.MaxHitPoints, character.CurrentHitPoints + intervals);
        character.HitPointsUpdatedAt = now;

        return true;
    }

    private readonly record struct DiceExpressionHolder(
        Models.Dice.DiceExpression? Damage,
        bool Finesse,
        Ability? Ability);
}
