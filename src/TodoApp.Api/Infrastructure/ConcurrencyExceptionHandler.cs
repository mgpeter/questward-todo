using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace TodoApp.Api.Infrastructure;

/// <summary>
/// Turns a lost optimistic-concurrency race into 409, not 500.
/// </summary>
/// <remarks>
/// The character row carries an xmin token, so two requests that both read the same balance and
/// both write it back no longer both succeed: the second one throws here instead of silently
/// overwriting the first, which is how one payment used to buy two affixes. The failed write
/// rolled back whole, so the client's own retry is a correct and complete repair, and 409 is
/// what tells it to make one.
/// </remarks>
public sealed class ConcurrencyExceptionHandler(ILogger<ConcurrencyExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        logger.LogInformation(
            exception, "Concurrent write to {Path} lost the race and was rolled back.",
            httpContext.Request.Path);

        await Results
            .Problem(
                "Something else changed your adventurer while that was in flight. Nothing was spent; try again.",
                statusCode: StatusCodes.Status409Conflict)
            .ExecuteAsync(httpContext);

        return true;
    }
}
