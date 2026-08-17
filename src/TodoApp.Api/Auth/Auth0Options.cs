using System.ComponentModel.DataAnnotations;

namespace TodoApp.Api.Auth;

/// <summary>
/// Auth0 tenant settings. All three values are public in a PKCE flow: there is no client
/// secret in this architecture and none may be added.
/// </summary>
public sealed class Auth0Options
{
    public const string SectionName = "Auth0";

    /// <summary>Tenant domain, for example <c>questward.eu.auth0.com</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Domain { get; set; } = string.Empty;

    /// <summary>API identifier configured in Auth0, for example <c>https://questward.api</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Client id of the SPA application. Served to the browser by /api/config.</summary>
    [Required(AllowEmptyStrings = false)]
    public string SpaClientId { get; set; } = string.Empty;

    /// <summary>Issuer URL. Auth0 issues tokens with a trailing slash, so it is kept.</summary>
    public string Authority => $"https://{Domain.Trim().TrimEnd('/')}/";
}
