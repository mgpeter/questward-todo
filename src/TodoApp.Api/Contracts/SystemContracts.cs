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
