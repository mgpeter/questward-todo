namespace TodoApp.Models;

/// <summary>
/// A local identity record. <see cref="Auth0Sub"/> is the only field tied to the identity
/// provider; everything else in the schema references <see cref="Id"/>, so swapping
/// providers means remapping one column rather than every foreign key.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// The OIDC subject claim. Provider-prefixed, for example <c>auth0|abc123</c>.
    /// The only identifier treated as stable; email and name are display data.
    /// </summary>
    public string Auth0Sub { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
