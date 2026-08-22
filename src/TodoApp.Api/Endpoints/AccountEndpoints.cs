using TodoApp.Api.Auth;
using TodoApp.Api.Contracts;
using TodoApp.Api.Services;
using TodoApp.Api.Validation;

namespace TodoApp.Api.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account")
            .WithTags("Account")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PerUser);

        // POST rather than DELETE, and on /reset rather than on the account itself, because the
        // account is not what is being deleted: the login survives and everything it ever
        // recorded does not. A DELETE /api/account would promise the opposite.
        //
        // The body is required and has to say the word. It is not security - anyone who can call
        // this is already signed in as the owner - it is the same guard a typed confirmation is
        // in the UI, one layer down, so a mis-wired client or a curious curl cannot empty an
        // account by accident.
        group.MapPost("/reset", Reset).ValidateBody<ResetAccountRequest>();

        return app;
    }

    private static async Task<IResult> Reset(
        ResetAccountRequest request,
        ICurrentUser currentUser,
        AccountService accounts,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken);

        await accounts.ResetAsync(user.Id, cancellationToken);

        return Results.NoContent();
    }
}
