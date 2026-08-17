using System.ComponentModel.DataAnnotations;

namespace TodoApp.Api.Contracts;

public sealed record CharacterDto(
    string Name,
    string AvatarKey,
    int Level,
    string Title,
    int TotalXp,
    int XpIntoLevel,
    int XpForNextLevel,
    int XpToNextLevel,
    int TasksCompleted,
    int AchievementsUnlocked,
    int AchievementsTotal,
    DateTimeOffset CreatedAt);

public sealed record UpdateCharacterRequest(
    [property: Required(AllowEmptyStrings = false, ErrorMessage = "A name is required.")]
    [property: StringLength(60, MinimumLength = 1)]
    string Name,
    [property: Required][property: StringLength(40)] string AvatarKey);
