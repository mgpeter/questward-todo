using Microsoft.Extensions.Options;
using TodoApp.Api.Auth;
using TodoApp.Api.Contracts;
using TodoApp.Api.Services;

namespace TodoApp.Api.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous by necessity: the SPA needs this before it can authenticate.
        app.MapGet("/api/config", GetConfig)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PerAddress)
            .WithTags("System");

        app.MapGet("/api/me", GetMe)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PerUser)
            .WithTags("System");

        return app;
    }

    /// <summary>
    /// Auth0 settings for the browser.
    /// </summary>
    /// <remarks>
    /// This endpoint exists because Vite inlines <c>import.meta.env.VITE_*</c> at build
    /// time and the SPA is built inside the Docker image. Baking the tenant in would tie
    /// one image to one tenant; serving it at runtime keeps the image portable.
    /// Only values that are public in a PKCE flow appear here.
    /// </remarks>
    private static IResult GetConfig(IOptions<Auth0Options> options)
    {
        var auth0 = options.Value;

        return Results.Ok(new ClientConfigDto(
            auth0.Domain,
            auth0.SpaClientId,
            auth0.Audience));
    }

    private static async Task<IResult> GetMe(
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        return Results.Ok(new MeDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.CreatedAt));
    }
}
