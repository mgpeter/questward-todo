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
            var weapon = loot.Grant(userId, characterClass.StartingWeaponKey, Rarity.Common);
            var armour = loot.Grant(userId, characterClass.StartingArmourKey, Rarity.Common);

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

        if (item.IsEquipped)
        {
            return RpgResult<InventoryItem>.Success(item);
        }

        // Clear the slot first. The partial unique index would reject the pair otherwise,
        // and doing it in one SaveChanges keeps the swap atomic.
        var occupying = await db.InventoryItems
            .Where(i => i.UserId == userId && i.Slot == item.Slot && i.IsEquipped)
            .ToListAsync(cancellationToken);

        foreach (var previous in occupying)
        {
            previous.IsEquipped = false;
        }

        item.IsEquipped = true;

        await db.SaveChangesAsync(cancellationToken);
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

        db.InventoryItems.Remove(item);

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
