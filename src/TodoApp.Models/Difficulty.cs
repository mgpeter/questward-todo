namespace TodoApp.Models;

/// <summary>
/// How much effort a task represents. Drives the XP award on completion.
/// </summary>
public enum Difficulty
{
    Easy = 0,
    Medium = 1,
    Hard = 2,
    Epic = 3
}

public static class DifficultyExtensions
{
    /// <summary>XP granted for completing a task of this difficulty.</summary>
    public static int BaseXp(this Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => 10,
        Difficulty.Medium => 25,
        Difficulty.Hard => 50,
        Difficulty.Epic => 100,
        _ => 10
    };
}
