using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace WebHealth.Infrastructure.Identity;

/// <summary>
/// Adds the display-name claim so the interface can greet the signed-in user by name instead of
/// by the sign-in email address. The claim is re-issued whenever the principal is regenerated.
/// </summary>
public sealed class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim(ApplicationClaimTypes.DisplayName, user.DisplayName));
        }

        return identity;
    }
}
