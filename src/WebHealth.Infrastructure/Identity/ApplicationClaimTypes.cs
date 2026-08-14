namespace WebHealth.Infrastructure.Identity;

/// <summary>
/// Claim types the application issues in addition to the ASP.NET Core Identity defaults.
/// </summary>
public static class ApplicationClaimTypes
{
    /// <summary>
    /// The user's display name. <see cref="System.Security.Claims.ClaimTypes.Name"/> carries the
    /// sign-in name, which is the email address, so the human-readable name needs its own claim.
    /// </summary>
    public const string DisplayName = "webhealth:display_name";
}
