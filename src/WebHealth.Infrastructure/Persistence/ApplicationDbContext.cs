using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Auditing;
using WebHealth.Infrastructure.Assignments;
using WebHealth.Infrastructure.Registry;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.Seo;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Health;
using WebHealth.Infrastructure.Maintenance;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Notifications;

namespace WebHealth.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<OwnerSubject> OwnerSubjects => Set<OwnerSubject>();

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<Website> Websites => Set<Website>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<WebsiteTag> WebsiteTags => Set<WebsiteTag>();

    public DbSet<WebsiteEnvironment> Environments => Set<WebsiteEnvironment>();

    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();

    public DbSet<Endpoint> Endpoints => Set<Endpoint>();

    public DbSet<TargetAuthorizationEvidence> TargetAuthorizations => Set<TargetAuthorizationEvidence>();

    public DbSet<EndpointMonitor> EndpointMonitors => Set<EndpointMonitor>();

    public DbSet<PolicyProfile> PolicyProfiles => Set<PolicyProfile>();

    public DbSet<LogicalCheck> LogicalChecks => Set<LogicalCheck>();

    public DbSet<CheckConfigurationSnapshot> CheckConfigurationSnapshots => Set<CheckConfigurationSnapshot>();

    public DbSet<ExecutionAttempt> ExecutionAttempts => Set<ExecutionAttempt>();

    public DbSet<ExecutionLease> ExecutionLeases => Set<ExecutionLease>();

    public DbSet<DurableWork> DurableWork => Set<DurableWork>();

    public DbSet<CheckResult> CheckResults => Set<CheckResult>();

    public DbSet<CertificateObservation> CertificateObservations => Set<CertificateObservation>();

    public DbSet<RedirectHop> RedirectHops => Set<RedirectHop>();

    public DbSet<Finding> Findings => Set<Finding>();

    public DbSet<IssueState> IssueStates => Set<IssueState>();

    public DbSet<EndpointHealth> EndpointHealth => Set<EndpointHealth>();

    public DbSet<MaintenanceWindow> MaintenanceWindows => Set<MaintenanceWindow>();

    public DbSet<MaintenanceTarget> MaintenanceTargets => Set<MaintenanceTarget>();

    public DbSet<MaintenanceOccurrence> MaintenanceOccurrences => Set<MaintenanceOccurrence>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<IncidentEvent> IncidentEvents => Set<IncidentEvent>();

    public DbSet<IncidentEvidence> IncidentEvidence => Set<IncidentEvidence>();

    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();

    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<NotificationReadMarker> NotificationReadMarkers => Set<NotificationReadMarker>();

    public DbSet<NotificationAttempt> NotificationAttempts => Set<NotificationAttempt>();

    public DbSet<SeoObservation> SeoObservations => Set<SeoObservation>();

    public DbSet<RobotsSnapshot> RobotsSnapshots => Set<RobotsSnapshot>();

    public DbSet<CrawlRun> CrawlRuns => Set<CrawlRun>();

    public DbSet<CrawlLinkResult> CrawlLinkResults => Set<CrawlLinkResult>();

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
