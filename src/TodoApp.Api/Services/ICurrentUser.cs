using TodoApp.Models;

namespace TodoApp.Api.Services;

/// <summary>
/// Resolves the caller to a local <see cref="User"/>.
/// </summary>
/// <remarks>
/// This is the only seam that knows how a caller is identified. Everything downstream
/// sees an internal <c>UserId</c> and nothing else, which is what keeps the identity
/// provider swappable (DEC-011).
/// </remarks>
public interface ICurrentUser
{
    Task<User> GetAsync(CancellationToken cancellationToken);
}
