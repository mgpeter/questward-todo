namespace TodoApp.Models.Progression;

/// <summary>
/// The XP -> level mapping. Cumulative XP required to reach level L is 25 * L * (L - 1),
/// so level 2 lands at 50 XP, level 3 at 150, level 4 at 300, level 10 at 2250.
/// Two Medium tasks earn the first level up; twelve reach level 4.
/// </summary>
public static class LevelCurve
{
    private const int XpCoefficient = 25;

    /// <summary>Highest level the curve is defined for. Well beyond any realistic total.</summary>
    public const int MaxLevel = 9_000;

    /// <summary>Cumulative XP needed to have reached <paramref name="level"/>.</summary>
    public static int XpForLevel(int level)
    {
        if (level <= 1)
        {
            return 0;
        }

        var exact = (long)XpCoefficient * level * (level - 1);
        return exact >= int.MaxValue ? int.MaxValue : (int)exact;
    }

    /// <summary>The level a character with <paramref name="totalXp"/> has reached.</summary>
    public static int LevelForXp(int totalXp)
    {
        if (totalXp <= 0)
        {
            return 1;
        }

        // Inverse of the quadratic, then nudged to correct any floating point drift.
        var estimate = (int)Math.Floor((1 + Math.Sqrt(1 + 4.0 * totalXp / XpCoefficient)) / 2.0);
        var level = Math.Clamp(estimate, 1, MaxLevel);

        while (level < MaxLevel && XpForLevel(level + 1) <= totalXp)
        {
            level++;
        }

        while (level > 1 && XpForLevel(level) > totalXp)
        {
            level--;
        }

        return level;
    }

    /// <summary>Everything the UI needs to draw a progress bar for a given XP total.</summary>
    public static LevelProgress Describe(int totalXp)
    {
        var xp = Math.Max(0, totalXp);
        var level = LevelForXp(xp);
        var levelFloor = XpForLevel(level);
        var levelCeiling = XpForLevel(level + 1);
        var span = Math.Max(1, levelCeiling - levelFloor);
        var into = xp - levelFloor;

        return new LevelProgress(
            Level: level,
            Title: RankTitles.ForLevel(level),
            TotalXp: xp,
            XpIntoLevel: into,
            XpForNextLevel: span,
            XpToNextLevel: Math.Max(0, span - into),
            LevelFloorXp: levelFloor,
            NextLevelXp: levelCeiling);
    }
}

public readonly record struct LevelProgress(
    int Level,
    string Title,
    int TotalXp,
    int XpIntoLevel,
    int XpForNextLevel,
    int XpToNextLevel,
    int LevelFloorXp,
    int NextLevelXp);
