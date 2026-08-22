using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services;
using TodoApp.Api.Services.Rpg;
using TodoApp.Models;
using TodoApp.Tests.Infrastructure;

namespace TodoApp.Tests.Rpg;

/// <summary>
/// Stamina is the gate that makes the RPG layer a sink for real work rather than a
/// substitute for it (DEC-012). An unbounded source of stamina is an unbounded source of
/// fights, and therefore of gold and loot, so the ledger has to balance exactly.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class StaminaLedgerTests(PostgresFixture postgres)
{
    private sealed record Harness(TodoApp.Data.TodoDbContext Db, GamificationService Gamification, Guid UserId);

    private async Task<Harness> ArrangeAsync()
    {
        await postgres.ResetAsync();
        var user = await postgres.CreateUserAsync("test|ledger");

        var db = postgres.CreateContext();
        var loot = new LootService(db, new FixedDiceRoller(10));
        var quests = new QuestService(db, loot, new ChronicleService(db));
        var gamification = new GamificationService(db, new AchievementEvaluator(), quests, new ChronicleService(db));

        return new Harness(db, gamification, user.Id);
    }

    private async Task<TodoTask> AddTaskAsync(Harness harness, Difficulty difficulty)
    {
        var task = new TodoTask { UserId = harness.UserId, Title = "Work", Difficulty = difficulty };

        harness.Db.Tasks.Add(task);
        await harness.Db.SaveChangesAsync();

        return task;
    }

    [Fact]
    public async Task Reopening_hands_back_the_stamina_it_granted()
    {
        var harness = await ArrangeAsync();
        var task = await AddTaskAsync(harness, Difficulty.Epic);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        var afterComplete = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);
        Assert.Equal(Difficulty.Epic.Stamina(), afterComplete.Stamina);

        await harness.Gamification.ReopenAsync(harness.UserId, task.Id, default);

        var afterReopen = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        Assert.Equal(0, afterReopen.Stamina);
        Assert.Equal(0, afterReopen.TotalXp);
    }

    [Fact]
    public async Task A_complete_reopen_loop_cannot_mint_stamina()
    {
        // The exploit this test exists for: reopening used to refund XP and keep the
        // stamina, so this loop printed 5 stamina and 5 hit points a cycle out of nothing.
        var harness = await ArrangeAsync();
        var task = await AddTaskAsync(harness, Difficulty.Epic);

        for (var cycle = 0; cycle < 25; cycle++)
        {
            await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);
            await harness.Gamification.ReopenAsync(harness.UserId, task.Id, default);
        }

        var character = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        Assert.Equal(0, character.Stamina);
        Assert.Equal(0, character.TotalXp);
    }

    [Fact]
    public async Task The_refund_uses_the_snapshot_not_the_current_difficulty()
    {
        // Same reasoning as XpAwarded: editing a task after finishing it must not change
        // what reopening hands back, or editing difficulty becomes a stamina lever.
        var harness = await ArrangeAsync();
        var task = await AddTaskAsync(harness, Difficulty.Easy);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        var tracked = await harness.Db.Tasks.SingleAsync(t => t.Id == task.Id);
        tracked.Difficulty = Difficulty.Epic;
        await harness.Db.SaveChangesAsync();

        await harness.Gamification.ReopenAsync(harness.UserId, task.Id, default);

        var character = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        // Granted 1 as Easy, so 1 comes back, not the 5 an Epic would have paid.
        Assert.Equal(0, character.Stamina);
    }

    [Fact]
    public async Task Reopening_never_reduces_a_character_below_one_hit_point()
    {
        var harness = await ArrangeAsync();
        var task = await AddTaskAsync(harness, Difficulty.Epic);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.CurrentHitPoints = 2;
        await harness.Db.SaveChangesAsync();

        await harness.Gamification.ReopenAsync(harness.UserId, task.Id, default);

        var after = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        Assert.True(after.CurrentHitPoints >= 1);
    }

    [Fact]
    public async Task Spent_stamina_is_not_clawed_back_into_a_negative_balance()
    {
        // Complete, spend the stamina on a fight, then reopen. The balance floors at zero
        // rather than going negative and locking the character out of the game.
        var harness = await ArrangeAsync();
        var task = await AddTaskAsync(harness, Difficulty.Epic);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        var character = await harness.Db.Characters.SingleAsync(c => c.UserId == harness.UserId);
        character.Stamina = 0; // spent it all on encounters
        await harness.Db.SaveChangesAsync();

        await harness.Gamification.ReopenAsync(harness.UserId, task.Id, default);

        var after = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);

        Assert.Equal(0, after.Stamina);
    }

    [Theory]
    [InlineData(Difficulty.Easy, 1)]
    [InlineData(Difficulty.Medium, 2)]
    [InlineData(Difficulty.Hard, 3)]
    [InlineData(Difficulty.Epic, 5)]
    public async Task Every_difficulty_balances(Difficulty difficulty, int expected)
    {
        var harness = await ArrangeAsync();
        var task = await AddTaskAsync(harness, difficulty);

        await harness.Gamification.CompleteAsync(harness.UserId, task.Id, 0, default);

        var completed = await harness.Db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal(expected, completed.StaminaAwarded);

        await harness.Gamification.ReopenAsync(harness.UserId, task.Id, default);

        var character = await harness.Db.Characters.AsNoTracking()
            .SingleAsync(c => c.UserId == harness.UserId);
        Assert.Equal(0, character.Stamina);
    }
}
