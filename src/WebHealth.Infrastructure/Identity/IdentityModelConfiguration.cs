using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace WebHealth.Infrastructure.Identity;

internal static class IdentityModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("app_user_role");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("app_user_claim");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("app_user_login");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("app_role_claim");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("app_user_token");
    }
}
