using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TodoApp.Tests.Infrastructure;

/// <summary>
/// Stands in for the Auth0 JWT bearer handler so tests never call a real tenant.
/// The subject is taken from a request header, which is what lets a single test act as
/// two different users and assert they cannot see each other's data.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SubjectHeader = "X-Test-Subject";
    public const string EmailHeader = "X-Test-Email";
    public const string NameHeader = "X-Test-Name";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubjectHeader, out var subject) ||
            string.IsNullOrWhiteSpace(subject))
        {
            // No header means an anonymous caller, so authorization produces a 401 exactly
            // as it would with a missing bearer token.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims = [new("sub", subject.ToString())];

        if (Request.Headers.TryGetValue(EmailHeader, out var email) && !string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim("email", email.ToString()));
        }

        if (Request.Headers.TryGetValue(NameHeader, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            claims.Add(new Claim("name", name.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, nameType: "sub", roleType: null);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
