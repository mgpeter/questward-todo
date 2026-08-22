using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models.Progression;
using TodoApp.Models.Rpg;

namespace TodoApp.Api.Services.Rpg;

/// <summary>What one ascension took and what it paid.</summary>
public sealed record AscendResult(
    int EssenceGained,
    int Essence,
    int Ascensions,
    int LevelReached,
    int GoldConverted,
    int StaminaConverted);

/// <summary>
/// Begins the character again, and renders the era that ended into essence.
/// </summary>
/// <remarks>
/// Destructive, deliberately, and recorded as such in DEC-021. The task-model spec put
/// ascension out of scope on the grounds that it was "destructive, irreversible and
/// row-deleting", and that ruling is being overturned rather than worked around: it is all three.
/// What makes it acceptable now is the chronicle, which is rows of its own and is the one thing
/// an ascension does not touch, so the era survives as a history even though nothing of it
/// survives as a balance.
/// <para>
/// Essence is the only payout, and the third thing in the app that moves it after salvage and
/// the forge. It buys affixes and nothing else: no XP (DEC-012), no stamina (DEC-003), no
/// completion. <c>TasksCompleted</c> is untouched for the same reason, being a count of real
/// work rather than a balance, and the achievements it feeds stay unlocked.
/// </para>
/// </remarks>
public sealed class AscendService(
    TodoDbContext db,
    CharacterSheetService sheets,
    AdventurerService adventurers,
    ChronicleService chronicle)
{
    public async Task<RpgResult<AscendResult>> AscendAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var character = await db.Characters.SingleAsync(c => c.UserId == userId, cancellationToken);
        var level = LevelCurve.LevelForXp(character.TotalXp);

        if (!AscendRules.MayAscend(level))
        {
            return RpgResult<AscendResult>.Fail(
                RpgFailure.NotReadyToAscend,
                $"Ascending opens at level {AscendRules.MinimumLevel}. You are level {level}.");
        }

        // Refused rather than resolved. The wipe below deletes the encounter, and a fight open in
        // another tab would then answer 404 in the middle of a round with no explanation; asking
        // the player to finish or walk out of it first costs them one click and is legible.
        if (await db.Encounters.AnyAsync(
                e => e.UserId == userId && e.Status == EncounterStatus.Active, cancellationToken))
        {
            return RpgResult<AscendResult>.Fail(
                RpgFailure.EncounterAlreadyActive,
                "Finish or leave the fight you are in before you ascend.");
        }

        var gold = character.Gold;
        var stamina = character.Stamina;
        var essence = AscendRules.EssenceFor(gold, stamina, level);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The entry is written before the wipe rather than after, so its era is the era being
        // left and the feed's divider falls in the right place. It is also the last thing that
        // will remember any of this.
        chronicle.Record(
            character,
            ChronicleKind.Ascended,
            new Dictionary<string, string>
            {
                [ChronicleNarrator.LevelKey] = level.ToString(),
                [ChronicleNarrator.GoldKey] = gold.ToString(),
                [ChronicleNarrator.StaminaKey] = stamina.ToString(),
                [ChronicleNarrator.EssenceKey] = essence.ToString(),
                [ChronicleNarrator.OrdinalKey] = (character.Ascensions + 1).ToString()
            });

        await db.SaveChangesAsync(cancellationToken);

        await WipeAsync(userId, cancellationToken);

        character.TotalXp = 0;
        character.Gold = 0;
        character.Stamina = 0;
        character.Essence += essence;
        character.Ascensions++;
        character.AscendedAt = DateTimeOffset.UtcNow;

        // The class stays, and with it the name and the avatar: what ascending resets is the
        // record of what this character did, not who they are. The scores go back to the class
        // spread because they were never anything else.
        if (ClassCatalog.Find(character.ClassKey) is { } characterClass)
        {
            character.AbilityScores = characterClass.StartingScores;

            // Every item is gone, so the class weapon and armour are granted again. Not a
            // printer: the inventory this fills was emptied one statement ago.
            await adventurers.GrantStartingGearAsync(userId, characterClass, cancellationToken);
        }

        // Full, and at the new level's maximum rather than the old one's. Beginning again on one
        // hit point would mean the first thing a new era asks of a player is to wait.
        character.HitPointsUpdatedAt = null;

        await db.SaveChangesAsync(cancellationToken);

        var sheet = await sheets.BuildAsync(character, cancellationToken);

        if (CharacterSheetService.NormaliseHitPoints(character, sheet, DateTimeOffset.UtcNow))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return RpgResult<AscendResult>.Success(new AscendResult(
            EssenceGained: essence,
            Essence: character.Essence,
            Ascensions: character.Ascensions,
            LevelReached: level,
            GoldConverted: gold,
            StaminaConverted: stamina));
    }

    /// <summary>
    /// Deletes the era: gear, fights, runs, contracts, quest progress, shop history and the
    /// bestiary.
    /// </summary>
    /// <remarks>
    /// Encounters go before runs, because an encounter cascades from the run it was a room of and
    /// deleting the runs first would take the fights with them before the chronicle's SET NULL
    /// had a chance to run on this connection's own statement order. Everything here is a
    /// <c>ExecuteDeleteAsync</c>, which bypasses the change tracker: that is what makes the
    /// chronicle's referential action the thing that protects the journal, rather than any code
    /// remembering to protect it.
    /// <para>
    /// What is deliberately absent: <c>chronicle_entries</c>, <c>achievement_unlocks</c> and
    /// <c>tasks</c>. Two of those are the record of real work, and the third is the app.
    /// </para>
    /// <para>
    /// The bestiary goes, and lore with it, since lore is derived from sightings. That is the
    /// one loss that is arguable, and it is the choice the product owner made: a new era meets
    /// its monsters again.
    /// </para>
    /// </remarks>
    private async Task WipeAsync(Guid userId, CancellationToken cancellationToken)
    {
        await db.Encounters.Where(e => e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.DungeonRuns.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.HuntContracts.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.QuestProgress.Where(q => q.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.InventoryItems.Where(i => i.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ShopPurchases.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ShopRerolls.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.BestiaryEntries.Where(b => b.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
