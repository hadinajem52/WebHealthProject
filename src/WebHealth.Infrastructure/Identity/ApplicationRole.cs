using Microsoft.AspNetCore.Identity;

namespace WebHealth.Infrastructure.Identity;

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public long Version { get; set; }
}
