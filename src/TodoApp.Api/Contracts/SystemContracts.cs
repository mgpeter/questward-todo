using System.ComponentModel.DataAnnotations;

namespace TodoApp.Api.Contracts;

/// <summary>
/// Public Auth0 configuration handed to the SPA at runtime. Never carries a secret:
/// the PKCE flow has no client secret and none may be introduced here.
/// </summary>
public sealed record ClientConfigDto(
    string Auth0Domain,
    string Auth0ClientId,
    string Auth0Audience);

public sealed record MeDto(
    Guid Id,
    string? Email,
    string? DisplayName,
    DateTimeOffset CreatedAt);

/// <summary>
/// The word, typed. Everything this account has recorded is deleted when it matches.
/// </summary>
/// <param name="Confirm">
/// Must be exactly <c>RESET</c>. A required field with one accepted value rather than a boolean,
/// because a boolean is what a client sends by accident.
/// </param>
public sealed record ResetAccountRequest(
    [property: Required(AllowEmptyStrings = false, ErrorMessage = "Type RESET to confirm.")]
    [property: RegularExpression("^RESET$", ErrorMessage = "Type RESET to confirm.")]
    string Confirm);
