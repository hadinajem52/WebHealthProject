using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Auditing;
using WebHealth.Infrastructure.Assignments;

namespace WebHealth.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<OwnerSubject> OwnerSubjects => Set<OwnerSubject>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        DatabaseConventions.Configure(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        IdentityModelConfiguration.Apply(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        DatabaseConventions.Apply(modelBuilder);
    }
}
