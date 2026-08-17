using System.ComponentModel.DataAnnotations;

namespace TodoApp.Api.Validation;

/// <summary>
/// Runs DataAnnotations over the first argument of type <typeparamref name="T"/> and
/// short-circuits with a ProblemDetails validation payload when it fails.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();

        if (argument is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["A request body is required."]
            });
        }

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(
            argument,
            new ValidationContext(argument),
            results,
            validateAllProperties: true);

        if (valid)
        {
            return await next(context);
        }

        var errors = results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => (Member: member, result.ErrorMessage)))
            .GroupBy(entry => entry.Member, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.ErrorMessage ?? "Invalid value.").ToArray(),
                StringComparer.Ordinal);

        return Results.ValidationProblem(errors);
    }
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder ValidateBody<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>()
            .ProducesValidationProblem();
}
