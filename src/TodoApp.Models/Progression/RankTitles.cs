namespace TodoApp.Models.Progression;

/// <summary>Flavour titles awarded in bands as the character levels up.</summary>
public static class RankTitles
{
    private static readonly (int MinLevel, string Title)[] Bands =
    [
        (30, "Legend"),
        (23, "Champion"),
        (17, "Master"),
        (12, "Expert"),
        (8, "Journeyman"),
        (5, "Adept"),
        (3, "Apprentice"),
        (1, "Novice")
    ];

    public static string ForLevel(int level)
    {
        foreach (var (minLevel, title) in Bands)
        {
            if (level >= minLevel)
            {
                return title;
            }
        }

        return "Novice";
    }
}
