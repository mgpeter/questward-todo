using TodoApp.Api.Contracts;
using TodoApp.Api.Mapping;
using TodoApp.Api.Services;
using TodoApp.Api.Validation;
using TodoApp.Data;

namespace TodoApp.Api.Endpoints;

public static class CharacterEndpoints
{
    public static IEndpointRouteBuilder MapCharacterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/character").WithTags("Character");

        group.MapGet("/", GetCharacter);
        group.MapPut("/", UpdateCharacter).ValidateBody<UpdateCharacterRequest>();

        return app;
    }

    private static async Task<IResult> GetCharacter(
        GamificationService gamification,
        CancellationToken cancellationToken)
    {
        var character = await gamification.GetCharacterAsync(cancellationToken);
        var unlocked = await gamification.CountUnlockedAsync(cancellationToken);

        return Results.Ok(character.ToDto(unlocked));
    }

    private static async Task<IResult> UpdateCharacter(
        UpdateCharacterRequest request,
        GamificationService gamification,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        var character = await gamification.GetCharacterAsync(cancellationToken);

        character.Name = request.Name.Trim();
        character.AvatarKey = request.AvatarKey.Trim();

        await db.SaveChangesAsync(cancellationToken);

        var unlocked = await gamification.CountUnlockedAsync(cancellationToken);

        return Results.Ok(character.ToDto(unlocked));
    }
}
