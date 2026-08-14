using System.Security.Claims;

namespace WebHealth.Infrastructure.Identity;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Returns the signed-in user's display name, falling back to the sign-in name for principals
    /// issued before the display-name claim existed.
    /// </summary>
    public static string? GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ApplicationClaimTypes.DisplayName) is { Length: > 0 } displayName
            ? displayName
            : principal.Identity?.Name;
}
