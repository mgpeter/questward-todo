using TodoApp.Api.Auth;
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
        var group = app.MapGroup("/api/character")
            .WithTags("Character")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PerUser);

        group.MapGet("/", GetCharacter);
        group.MapPut("/", UpdateCharacter).ValidateBody<UpdateCharacterRequest>();

        return app;
    }

    private static async Task<IResult> GetCharacter(
        GamificationService gamification,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var character = await gamification.GetCharacterAsync(user.Id, cancellationToken);
        var unlocked = await gamification.CountUnlockedAsync(user.Id, cancellationToken);

        return Results.Ok(character.ToDto(unlocked));
    }

    private static async Task<IResult> UpdateCharacter(
        UpdateCharacterRequest request,
        GamificationService gamification,
        ICurrentUser currentUser,
        TodoDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);
        var character = await gamification.GetCharacterAsync(user.Id, cancellationToken);

        character.Name = request.Name.Trim();
        character.AvatarKey = request.AvatarKey.Trim();

        await db.SaveChangesAsync(cancellationToken);

        var unlocked = await gamification.CountUnlockedAsync(user.Id, cancellationToken);

        return Results.Ok(character.ToDto(unlocked));
    }
}
