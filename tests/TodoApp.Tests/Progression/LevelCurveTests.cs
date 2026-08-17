using TodoApp.Models;
using TodoApp.Models.Progression;

namespace TodoApp.Tests.Progression;

public class LevelCurveTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 50)]
    [InlineData(3, 150)]
    [InlineData(4, 300)]
    [InlineData(5, 500)]
    [InlineData(6, 750)]
    [InlineData(10, 2250)]
    [InlineData(15, 5250)]
    public void XpForLevel_matches_the_documented_curve(int level, int expectedXp) =>
        Assert.Equal(expectedXp, LevelCurve.XpForLevel(level));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(49, 1)]
    [InlineData(50, 2)]   // exact threshold belongs to the higher level
    [InlineData(51, 2)]
    [InlineData(149, 2)]
    [InlineData(150, 3)]
    [InlineData(299, 3)]
    [InlineData(300, 4)]
    [InlineData(2250, 10)]
    public void LevelForXp_places_the_boundaries_where_the_curve_says(int totalXp, int expectedLevel) =>
        Assert.Equal(expectedLevel, LevelCurve.LevelForXp(totalXp));

    [Fact]
    public void LevelForXp_floors_at_one_for_zero_and_negative_totals()
    {
        Assert.Equal(1, LevelCurve.LevelForXp(0));
        Assert.Equal(1, LevelCurve.LevelForXp(-1));
        Assert.Equal(1, LevelCurve.LevelForXp(int.MinValue));
    }

    [Fact]
    public void LevelForXp_inverts_XpForLevel_exactly_across_the_whole_range()
    {
        // The closed-form inverse uses a square root, so this guards against floating
        // point drift putting a level one either side of its own threshold.
        for (var level = 1; level <= 500; level++)
        {
            var floor = LevelCurve.XpForLevel(level);

            Assert.Equal(level, LevelCurve.LevelForXp(floor));

            if (level > 1)
            {
                Assert.Equal(level - 1, LevelCurve.LevelForXp(floor - 1));
            }
        }
    }

    [Fact]
    public void XpForLevel_saturates_rather_than_overflowing()
    {
        // 25 * L * (L-1) overflows int well before MaxLevel, so it is computed as long
        // and clamped. A negative result here would mean the clamp regressed.
        Assert.True(LevelCurve.XpForLevel(LevelCurve.MaxLevel) > 0);
        Assert.Equal(int.MaxValue, LevelCurve.XpForLevel(int.MaxValue));
    }

    [Fact]
    public void Describe_reports_progress_within_the_current_level()
    {
        var progress = LevelCurve.Describe(175);

        Assert.Equal(3, progress.Level);
        Assert.Equal("Apprentice", progress.Title);
        Assert.Equal(175, progress.TotalXp);
        Assert.Equal(150, progress.LevelFloorXp);
        Assert.Equal(300, progress.NextLevelXp);
        Assert.Equal(25, progress.XpIntoLevel);
        Assert.Equal(150, progress.XpForNextLevel);
        Assert.Equal(125, progress.XpToNextLevel);
    }

    [Fact]
    public void Describe_treats_a_negative_total_as_zero()
    {
        var progress = LevelCurve.Describe(-500);

        Assert.Equal(1, progress.Level);
        Assert.Equal(0, progress.TotalXp);
        Assert.Equal(0, progress.XpIntoLevel);
        Assert.Equal(50, progress.XpToNextLevel);
    }

    [Fact]
    public void Describe_sits_exactly_on_a_threshold_with_no_progress_into_the_level()
    {
        var progress = LevelCurve.Describe(50);

        Assert.Equal(2, progress.Level);
        Assert.Equal(0, progress.XpIntoLevel);
        Assert.Equal(100, progress.XpForNextLevel);
        Assert.Equal(100, progress.XpToNextLevel);
    }

    [Theory]
    [InlineData(1, "Novice")]
    [InlineData(2, "Novice")]
    [InlineData(3, "Apprentice")]
    [InlineData(5, "Adept")]
    [InlineData(8, "Journeyman")]
    [InlineData(12, "Expert")]
    [InlineData(17, "Master")]
    [InlineData(23, "Champion")]
    [InlineData(30, "Legend")]
    [InlineData(999, "Legend")]
    public void RankTitles_awards_the_band_the_level_falls_into(int level, string expected) =>
        Assert.Equal(expected, RankTitles.ForLevel(level));

    [Theory]
    [InlineData(Difficulty.Easy, 10)]
    [InlineData(Difficulty.Medium, 25)]
    [InlineData(Difficulty.Hard, 50)]
    [InlineData(Difficulty.Epic, 100)]
    public void Difficulty_pays_the_documented_XP(Difficulty difficulty, int expectedXp) =>
        Assert.Equal(expectedXp, difficulty.BaseXp());

    [Fact]
    public void Two_medium_tasks_reach_level_two()
    {
        // The headline claim in the README and mission. If the curve is ever retuned,
        // this is the assertion that should force the docs to be updated too.
        var xp = Difficulty.Medium.BaseXp() * 2;

        Assert.Equal(2, LevelCurve.LevelForXp(xp));
    }
}
