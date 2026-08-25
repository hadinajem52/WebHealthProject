using System.Security.Claims;

namespace WebHealth.Infrastructure.Identity;

public static class ClaimsPrincipalExtensions
{
    public static string? GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ApplicationClaimTypes.DisplayName) is { Length: > 0 } displayName
            ? displayName
            : principal.Identity?.Name;
}
