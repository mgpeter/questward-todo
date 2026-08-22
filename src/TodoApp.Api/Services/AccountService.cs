using Microsoft.EntityFrameworkCore;
using TodoApp.Data;

namespace TodoApp.Api.Services;

/// <summary>
/// The danger zone: everything this account has ever recorded, deleted.
/// </summary>
/// <remarks>
/// The identity survives and nothing else does. Deleting the <c>users</c> row would work just as
/// well - every table cascades from it - but it would also mint a new <c>UserId</c> on the next
/// request, and "start again" should not silently become "become a different person" for anything
/// that ever recorded the old id.
/// <para>
/// What comes back is a character row at its construction defaults with no class, which is
/// exactly the state <c>UserProvisioner</c> creates on a first sign-in, so the app lands on class
/// select rather than on a half-empty adventure screen.
/// </para>
/// <para>
/// Distinct from ascending, which keeps the tasks, the badges and the journal because they are
/// the record of real work. This keeps nothing.
/// </para>
/// </remarks>
public sealed class AccountService(TodoDbContext db)
{
    public async Task ResetAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Encounters before runs, because an encounter cascades from the run it was a room of,
        // and before the chronicle for a reason that only matters here: the chronicle's foreign
        // key nulls on delete rather than cascading, so deleting it first would leave the fights
        // pointing at nothing for one statement. Both orders end at the same empty tables; this
        // one never has a row in an odd state in between.
        await db.Encounters.Where(e => e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ChronicleEntries.Where(e => e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.DungeonRuns.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.HuntContracts.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.QuestProgress.Where(q => q.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.BestiaryEntries.Where(b => b.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.InventoryItems.Where(i => i.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ShopPurchases.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ShopRerolls.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.AchievementUnlocks.Where(a => a.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // Subtasks are tasks, and both go. The self-referencing key is ON DELETE CASCADE, but
        // this deletes the whole set in one statement anyway.
        await db.Tasks.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        var character = await db.Characters.SingleOrDefaultAsync(
            c => c.UserId == userId, cancellationToken);

        if (character is not null)
        {
            character.Name = "Adventurer";
            character.AvatarKey = "fox";
            character.TotalXp = 0;
            character.TasksCompleted = 0;
            character.ClassKey = null;
            character.AbilityScores = new Models.Rpg.AbilityScores(10, 10, 10, 10, 10, 10);
            character.CurrentHitPoints = 0;
            character.Stamina = 0;
            character.Gold = 0;
            character.Essence = 0;
            character.HitPointsUpdatedAt = null;
            character.Ascensions = 0;
            character.AscendedAt = null;

            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
