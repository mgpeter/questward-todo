using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

public sealed record SellResult(int GoldGained, int Gold);

public sealed record RestResult(int GoldSpent, int Gold, int HitPoints, int MaxHitPoints);

/// <summary>
/// Class selection and the inventory: everything about the character outside a fight.
/// </summary>
public sealed class AdventurerService(TodoDbContext db, CharacterSheetService sheets, LootService loot)
{
    public async Task<RpgResult<Character>> ChooseClassAsync(
        Guid userId,
        string classKey,
        CancellationToken cancellationToken)
    {
        var characterClass = ClassCatalog.Find(classKey);

        if (characterClass is null)
        {
            return RpgResult<Character>.Fail(RpgFailure.UnknownClass, $"No class called '{classKey}'.");
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var isFirstChoice = character.ClassKey is null;

        character.ClassKey = characterClass.Key;
        character.AbilityScores = characterClass.StartingScores;

        // Starting gear is granted once. Changing class later re-rolls scores but does not
        // hand out a second set, or class-swapping becomes an item printer.
        if (isFirstChoice)
        {
            var weapon = await loot.GrantAsync(
                userId, characterClass.StartingWeaponKey, Rarity.Common, cancellationToken);
            var armour = await loot.GrantAsync(
                userId, characterClass.StartingArmourKey, Rarity.Common, cancellationToken);

            weapon.IsEquipped = true;
            armour.IsEquipped = true;
        }

        // Scores changed, so the maximum moved. Reset to full: choosing a class should
        // never leave someone worse off than before they chose.
        character.HitPointsUpdatedAt = null;

        await db.SaveChangesAsync(cancellationToken);

        var sheet = await sheets.BuildAsync(character, cancellationToken);

        if (CharacterSheetService.NormaliseHitPoints(character, sheet, DateTimeOffset.UtcNow))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return RpgResult<Character>.Success(character);
    }

    public Task<List<InventoryItem>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        db.InventoryItems
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.IsEquipped)
            .ThenByDescending(i => i.Rarity)
            .ThenByDescending(i => i.AcquiredAt)
            .ToListAsync(cancellationToken);

    public async Task<RpgResult<InventoryItem>> EquipAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, cancellationToken);

        if (item is null)
        {
            return RpgResult<InventoryItem>.Fail(RpgFailure.NotFound, "No such item.");
        }

        // A consumable is used, never worn. Without this it would occupy the Consumable arm of
        // the equipped-slot index and then reach the character sheet as though it were gear,
        // where a potion with no damage and no armour would silently contribute nothing while
        // the real item it displaced contributed nothing either.
        if (item.Slot == ItemSlot.Consumable)
        {
            return RpgResult<InventoryItem>.Fail(
                RpgFailure.ItemNotUsable, "That is drunk or thrown in a fight, not worn.");
        }

        if (item.IsEquipped)
        {
            return RpgResult<InventoryItem>.Success(item);
        }

        // Free the slot and take it in that order, in two saves, inside one transaction.
        //
        // Both writes used to be a single SaveChanges, on the assumption that the order they
        // were assigned in was the order they would be sent in. It is not: EF sorts a batch by
        // ascending key, and IsEquipped is only the filter on the equipped-slot index rather
        // than one of its columns, so nothing tells EF the two rows are related at all.
        // Whenever the taking row sorted first, Postgres saw two equipped rows in one slot and
        // refused the batch - a partial unique index is checked per row, and unlike a
        // constraint it cannot be deferred, because a constraint cannot carry a filter. It
        // surfaced as a 500 the inventory screen showed no sign of, so finding better armour
        // and pressing Equip did nothing at all, for about half of all pairs of items.
        //
        // Two saves rather than one ExecuteUpdate because that bypasses the change tracker:
        // the freed rows would stay equipped in memory, and the next equip in the same scope
        // would read its own stale copy and return early having done nothing.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var occupying = await db.InventoryItems
            .Where(i => i.UserId == userId && i.Slot == item.Slot && i.IsEquipped)
            .ToListAsync(cancellationToken);

        foreach (var previous in occupying)
        {
            previous.IsEquipped = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        item.IsEquipped = true;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await ClampHitPointsAsync(userId, cancellationToken);

        return RpgResult<InventoryItem>.Success(item);
    }

    public async Task<RpgResult<InventoryItem>> UnequipAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, cancellationToken);

        if (item is null)
        {
            return RpgResult<InventoryItem>.Fail(RpgFailure.NotFound, "No such item.");
        }

        item.IsEquipped = false;

        await db.SaveChangesAsync(cancellationToken);
        await ClampHitPointsAsync(userId, cancellationToken);

        return RpgResult<InventoryItem>.Success(item);
    }

    public async Task<RpgResult<SellResult>> SellAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, cancellationToken);

        if (item is null)
        {
            return RpgResult<SellResult>.Fail(RpgFailure.NotFound, "No such item.");
        }

        if (item.IsEquipped)
        {
            return RpgResult<SellResult>.Fail(
                RpgFailure.ItemEquipped, "Take it off before selling it.");
        }

        var definition = ItemCatalog.Find(item.ItemKey);

        // Half the notional value, which is the traditional rate for a shopkeeper who
        // knows you have nowhere else to go.
        var gold = definition is null ? 1 : Math.Max(1, definition.ValueAt(item.Rarity) / 2);

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        character.Gold += gold;

        // One unit, not the row. Selling one potion out of six used to take the other five with
        // it, because everything else in the bag is a row that means exactly one item.
        InventoryStack.ConsumeOne(db, item);

        await db.SaveChangesAsync(cancellationToken);

        return RpgResult<SellResult>.Success(new SellResult(gold, character.Gold));
    }

    /// <summary>
    /// A night at the tavern: pay gold, wake up whole.
    /// </summary>
    /// <remarks>
    /// The second gold sink, and the alternative to waiting out passive regeneration. The
    /// price is deterministic and shown before paying, so the choice between time and
    /// money is an informed one.
    /// </remarks>
    public async Task<RpgResult<RestResult>> RestAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await db.Encounters.AnyAsync(
                e => e.UserId == userId && e.Status == EncounterStatus.Active, cancellationToken))
        {
            return RpgResult<RestResult>.Fail(
                RpgFailure.EncounterAlreadyActive, "You cannot bed down mid-fight.");
        }

        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var sheet = await sheets.BuildAsync(character, cancellationToken);

        CharacterSheetService.NormaliseHitPoints(character, sheet, DateTimeOffset.UtcNow);

        var missing = sheet.MaxHitPoints - character.CurrentHitPoints;

        if (missing <= 0)
        {
            return RpgResult<RestResult>.Fail(
                RpgFailure.AlreadyAtFullHealth, "You are already in fighting shape.");
        }

        var cost = RestCost(missing, sheet.Level);

        if (character.Gold < cost)
        {
            return RpgResult<RestResult>.Fail(
                RpgFailure.NotEnoughGold,
                $"A room costs {cost} gold and you have {character.Gold}.");
        }

        character.Gold -= cost;
        character.CurrentHitPoints = sheet.MaxHitPoints;
        character.HitPointsUpdatedAt = DateTimeOffset.UtcNow;

        // Nothing here touches TotalXp: sleeping is not an achievement.
        await db.SaveChangesAsync(cancellationToken);

        return RpgResult<RestResult>.Success(
            new RestResult(cost, character.Gold, character.CurrentHitPoints, sheet.MaxHitPoints));
    }

    /// <summary>Deterministic and shown before paying, so the trade is an informed one.</summary>
    public static int RestCost(int missingHitPoints, int level) =>
        missingHitPoints <= 0 ? 0 : Math.Max(5, missingHitPoints * (2 + level));

    /// <summary>
    /// Equipment changes Constitution, which changes maximum hit points. Without this,
    /// unequipping a Constitution item leaves current above the new maximum.
    /// </summary>
    private async Task ClampHitPointsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var sheet = await sheets.BuildAsync(character, cancellationToken);

        if (CharacterSheetService.NormaliseHitPoints(character, sheet, DateTimeOffset.UtcNow))
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
