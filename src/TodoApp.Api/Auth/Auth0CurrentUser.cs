using System.Security.Claims;
using TodoApp.Api.Services;
using TodoApp.Models;

namespace TodoApp.Api.Auth;

/// <summary>
/// Resolves the authenticated principal to a local user.
/// </summary>
/// <remarks>
/// The only place in the codebase that reads the OIDC <c>sub</c> claim. Everything
/// downstream works with an internal <c>UserId</c>, which is what makes the identity
/// provider swappable without touching endpoints or services (DEC-011).
/// </remarks>
public sealed class Auth0CurrentUser(
    IHttpContextAccessor httpContextAccessor,
    UserProvisioner provisioner) : ICurrentUser
{
    public Task<User> GetAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            // Reaching here means an endpoint was mapped without RequireAuthorization.
            // Failing loudly is right: the alternative is silently serving someone
            // else's data or inventing an identity.
            throw new InvalidOperationException(
                "No authenticated user on the request. Every endpoint resolving a current " +
                "user must be behind RequireAuthorization().");
        }

        // MapInboundClaims is disabled, so claims arrive under their OIDC names rather
        // than being rewritten to the legacy ClaimTypes.* URIs.
        var subject = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Token contained no 'sub' claim.");

        return provisioner.GetOrCreateAsync(
            subject,
            principal.FindFirstValue("email"),
            principal.FindFirstValue("name") ?? principal.FindFirstValue("nickname"),
            cancellationToken);
    }
}
