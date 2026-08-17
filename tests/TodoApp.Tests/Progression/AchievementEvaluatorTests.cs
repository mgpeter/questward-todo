using TodoApp.Api.Services;
using TodoApp.Models;
using TodoApp.Models.Progression;

namespace TodoApp.Tests.Progression;

public class AchievementEvaluatorTests
{
    private readonly AchievementEvaluator _evaluator = new();

    /// <summary>
    /// A context that earns nothing, so each test can vary exactly one thing and the
    /// resulting badge is unambiguously attributable to that change.
    /// </summary>
    private static AchievementContext Baseline(
        Difficulty difficulty = Difficulty.Medium,
        int xpAwarded = 25,
        int tasksCompletedTotal = 1,
        int level = 1,
        int hardOrEpicCompleted = 0,
        int openTasksAfter = 4,
        int completedTodayLocal = 1,
        int localHour = 12) =>
        new(
            CompletedTask: new TodoTask { Difficulty = difficulty, XpAwarded = xpAwarded },
            TasksCompletedTotal: tasksCompletedTotal,
            Level: level,
            HardOrEpicCompleted: hardOrEpicCompleted,
            OpenTasksAfter: openTasksAfter,
            CompletedTodayLocal: completedTodayLocal,
            LocalCompletedAt: new DateTimeOffset(2026, 8, 17, localHour, 0, 0, TimeSpan.Zero));

    [Fact]
    public void First_completion_earns_First_Blood()
    {
        var earned = _evaluator.Evaluate(Baseline(tasksCompletedTotal: 1));

        Assert.Contains(AchievementCatalog.FirstBlood, earned);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void Getting_Started_needs_ten_tasks(int total, bool expected) =>
        Assert.Equal(expected, _evaluator.Evaluate(Baseline(tasksCompletedTotal: total))
            .Contains(AchievementCatalog.GettingStarted));

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    public void Centurion_needs_a_hundred_tasks(int total, bool expected) =>
        Assert.Equal(expected, _evaluator.Evaluate(Baseline(tasksCompletedTotal: total))
            .Contains(AchievementCatalog.Centurion));

    [Theory]
    [InlineData(Difficulty.Easy, false)]
    [InlineData(Difficulty.Medium, false)]
    [InlineData(Difficulty.Hard, false)]
    [InlineData(Difficulty.Epic, true)]
    public void Epic_Slayer_needs_an_Epic_task(Difficulty difficulty, bool expected) =>
        Assert.Equal(expected, _evaluator.Evaluate(Baseline(difficulty: difficulty))
            .Contains(AchievementCatalog.EpicSlayer));

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void Giant_Killer_needs_ten_hard_or_epic(int count, bool expected) =>
        Assert.Equal(expected, _evaluator.Evaluate(Baseline(hardOrEpicCompleted: count))
            .Contains(AchievementCatalog.GiantKiller));

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    public void Deep_Work_needs_a_fifty_XP_task(int xpAwarded, bool expected) =>
        Assert.Equal(expected, _evaluator.Evaluate(Baseline(xpAwarded: xpAwarded))
            .Contains(AchievementCatalog.DeepWork));

    [Fact]
    public void Deep_Work_reads_the_awarded_XP_not_the_current_difficulty()
    {
        // XP is snapshotted at completion (DEC-003). An Easy task that somehow banked
        // 50 XP still earned it; a task retitled to Epic after the fact did not.
        var snapshotted = _evaluator.Evaluate(Baseline(difficulty: Difficulty.Easy, xpAwarded: 50));
        Assert.Contains(AchievementCatalog.DeepWork, snapshotted);

        var relabelled = _evaluator.Evaluate(Baseline(difficulty: Difficulty.Epic, xpAwarded: 10));
        Assert.DoesNotContain(AchievementCatalog.DeepWork, relabelled);
    }

    [Theory]
    [InlineData(4, false, false, false)]
    [InlineData(5, true, false, false)]
    [InlineData(10, true, true, false)]
    [InlineData(25, true, true, true)]
    [InlineData(30, true, true, true)]
    public void Level_badges_unlock_at_their_thresholds(
        int level,
        bool five,
        bool ten,
        bool twentyFive)
    {
        var earned = _evaluator.Evaluate(Baseline(level: level));

        Assert.Equal(five, earned.Contains(AchievementCatalog.Level5));
        Assert.Equal(ten, earned.Contains(AchievementCatalog.Level10));
        Assert.Equal(twentyFive, earned.Contains(AchievementCatalog.Level25));
    }

    [Theory]
    [InlineData(0, 2, false)]  // board empty, but the day was quiet
    [InlineData(0, 3, true)]   // board empty after three completions today
    [InlineData(0, 7, true)]
    [InlineData(1, 5, false)]  // busy day, but something is still open
    public void Clean_Slate_needs_an_empty_board_and_a_productive_day(
        int openAfter,
        int completedToday,
        bool expected) =>
        Assert.Equal(expected, _evaluator.Evaluate(
            Baseline(openTasksAfter: openAfter, completedTodayLocal: completedToday))
            .Contains(AchievementCatalog.CleanSlate));

    [Fact]
    public void Clean_Slate_is_reachable_by_completing_tasks_one_at_a_time()
    {
        // Regression: the original rule was `OpenTasksAfter == 0 && OpenTasksBefore >= 3`.
        // Tasks are completed one at a time, so OpenTasksAfter is always OpenTasksBefore-1
        // and reaching zero implies exactly one was open. The badge could never be earned.
        // This walks the real sequence of clearing a three-task board.
        var lastCompletion = _evaluator.Evaluate(Baseline(
            openTasksAfter: 0,
            completedTodayLocal: 3));

        Assert.Contains(AchievementCatalog.CleanSlate, lastCompletion);
    }

    [Theory]
    [InlineData(0, true, true)]    // small hours earn both
    [InlineData(3, true, true)]
    [InlineData(4, false, true)]   // past the owl window, still early
    [InlineData(5, false, true)]
    [InlineData(6, false, false)]
    [InlineData(23, false, false)]
    public void Time_of_day_badges_use_the_local_hour(int hour, bool nightOwl, bool earlyBird)
    {
        var earned = _evaluator.Evaluate(Baseline(localHour: hour));

        Assert.Equal(nightOwl, earned.Contains(AchievementCatalog.NightOwl));
        Assert.Equal(earlyBird, earned.Contains(AchievementCatalog.EarlyBird));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void Productive_Day_needs_five_in_one_local_day(int completedToday, bool expected) =>
        Assert.Equal(expected, _evaluator.Evaluate(Baseline(completedTodayLocal: completedToday))
            .Contains(AchievementCatalog.ProductiveDay));

    [Fact]
    public void Every_key_the_evaluator_can_emit_exists_in_the_catalog()
    {
        // Guards the string keys: a typo here would persist an unlock row that the
        // achievements endpoint silently drops, so the badge would never appear.
        var everything = _evaluator.Evaluate(Baseline(
            difficulty: Difficulty.Epic,
            xpAwarded: 100,
            tasksCompletedTotal: 100,
            level: 30,
            hardOrEpicCompleted: 10,
            openTasksAfter: 0,
            completedTodayLocal: 5,
            localHour: 2));

        Assert.All(everything, key => Assert.NotNull(AchievementCatalog.Find(key)));
        Assert.Equal(AchievementCatalog.All.Count, everything.Distinct().Count());
    }

    [Fact]
    public void A_typical_first_task_earns_only_First_Blood()
    {
        var earned = _evaluator.Evaluate(Baseline());

        Assert.Equal([AchievementCatalog.FirstBlood], earned);
    }
}
