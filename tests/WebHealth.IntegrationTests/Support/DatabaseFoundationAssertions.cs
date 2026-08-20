using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Identity;
using Npgsql;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Application.Administration;
using WebHealth.Application.Auditing;
using WebHealth.Application.Assignments;
using WebHealth.Infrastructure.Assignments;
using WebHealth.Infrastructure.Auditing;
using WebHealth.Application.Registry;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Maintenance;
using WebHealth.Application.Incidents;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Domain.Health;
using WebHealth.Domain.Incidents;
using System.Text;
using System.Threading;
using WebHealth.Domain.Maintenance;
using WebHealth.Domain.Seo;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Registry;
using WebHealth.Infrastructure.Seo;
using WebHealth.Infrastructure.Health;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Maintenance;
using WebHealth.Application.Notifications;
using WebHealth.Domain.Notifications;
using WebHealth.Infrastructure.Notifications;
using Xunit;
using System.Text.Json;
using System.Collections.Concurrent;

namespace WebHealth.IntegrationTests.Support;

internal static class DatabaseFoundationAssertions
{
    /// <summary>
    /// Spacing for fixture checks. It is allocated here rather than passed in by each caller: a
    /// hand-picked number is a namespace every stage has to share, and one that grew past the
    /// window its base allowed silently dated a check into the future.
    /// </summary>
    private static int fixtureSequence;

    private static readonly string[] ExpectedMigrations =
    [
        "20260813095149_InitialFoundation",
        "20260814190445_IdentityAccessAndAudit",
        "20260814190510_RegistryFoundation",
        "20260816120256_MonitoringExecutionFoundation",
        "20260816175236_HttpMonitoringHistory",
        "20260817070044_LogicalCheckExecutionLifecycle",
        "20260817072634_HangfireSchedulingAndRecovery",
        "20260817103231_HealthMaintenanceAndIncidents",
        "20260817120619_IncidentLifecycle",
        "20260817130137_DurableNotifications",
        "20260818065727_EndpointSchedulingMode",
        "20260818081749_NotificationReadMarker",
        "20260818084805_NotificationRecipientIndex",
        "20260818101710_SslCertificateMonitoring",
        "20260818110028_SslSeverityAndPerformanceRules",
        "20260818185101_ReportingSampleMonitorIndex",
        "20260819094132_RecurringMaintenanceOccurrences",
        "20260819101929_SeoValueExtraction",
        "20260819111509_SeoConfigurationAndRobotsPolicy",
        "20260819162313_CrawlRunsAndLinkResults",
        "20260819165856_CrawlRunConfigurationSnapshot",
        "20260820083941_EndpointPurgeEvidenceRemoval"
    ];

    private static readonly string[] ExpectedTables =
    [
        "audit_event",
        "access_grant",
        "client",
        "check_configuration_snapshot",
        "certificate_observation",
        "check_result",
        "durable_work",
        "environment",
        "endpoint",
        "endpoint_monitor",
        "execution_attempt",
        "execution_lease",
        "logical_check",
        "policy_profile",
        "redirect_hop",
        "finding",
        "tag",
        "website",
        "website_tag",
        "app_role",
        "app_role_claim",
        "app_user",
        "app_user_claim",
        "app_user_login",
        "app_user_role",
        "app_user_token"
        ,"owner_subject"
        ,"team"
        ,"team_member"
        ,"target_authorization"
        ,"issue_state"
        ,"endpoint_health"
        ,"maintenance_window"
        ,"maintenance_target"
        ,"maintenance_occurrence"
        ,"incident"
        ,"incident_event"
        ,"incident_evidence"
        ,"notification_event"
        ,"notification_delivery"
        ,"notification_attempt"
        ,"notification_read_marker"
        ,"seo_observation"
        ,"robots_snapshot"
        ,"crawl_run"
        ,"crawl_link_result"
    ];

    // The tables added by the three Phase 4 migrations (HealthMaintenanceAndIncidents,
    // IncidentLifecycle's table-shape is a superset of the same tables, DurableNotifications) —
    // used to compute the expected table set at the Phase 3 boundary checkpoint.
    /// <summary>
    /// Every table created after the Phase 3 boundary, which is what the upgrade check migrates
    /// back down to. Each phase that adds a table adds it here, or the down-level schema
    /// comparison starts expecting a table that migration never created.
    /// </summary>
    private static readonly string[] TablesAddedAfterPhaseThree =
    [
        "issue_state", "endpoint_health", "maintenance_window", "maintenance_target",
        "maintenance_occurrence", "incident", "incident_event", "incident_evidence",
        "notification_event", "notification_delivery", "notification_attempt",
        "notification_read_marker", "certificate_observation", "seo_observation", "robots_snapshot",
        "crawl_run", "crawl_link_result"
    ];

    private static readonly string[] ExpectedEntityTypeNames =
    [
        // Identity entities
        "IdentityRoleClaim`1",
        "IdentityUserClaim`1",
        "IdentityUserLogin`1",
        "IdentityUserRole`1",
        "IdentityUserToken`1",
        "ApplicationRole",
        "ApplicationUser",
        // Administration
        "OwnerSubject",
        "Team",
        "TeamMember",
        "AuditEvent",
        // Registry
        "Client",
        "Website",
        "Tag",
        "WebsiteTag",
        "WebsiteEnvironment",
        "AccessGrant",
        "Endpoint",
        "TargetAuthorizationEvidence",
        "EndpointMonitor",
        "PolicyProfile",
        // Monitoring
        "EndpointHealth",
        "IssueState",
        "CertificateObservation",
        "CheckConfigurationSnapshot",
        "CheckResult",
        "DurableWork",
        "ExecutionAttempt",
        "ExecutionLease",
        "Finding",
        "LogicalCheck",
        "RedirectHop",
        // Incidents
        "Incident",
        "IncidentEvent",
        "IncidentEvidence",
        // Maintenance
        "MaintenanceOccurrence",
        "MaintenanceTarget",
        "MaintenanceWindow",
        // Notifications
        "NotificationAttempt",
        "NotificationDelivery",
        "NotificationEvent",
        "NotificationReadMarker",
        // SEO
        "SeoObservation",
        "RobotsSnapshot",
        // Crawler
        "CrawlRun",
        "CrawlLinkResult"
    ];

    public static async Task VerifyAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var context = new ApplicationDbContext(options.Options);

        await context.Database.MigrateAsync();

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await context.Database.GetAppliedMigrationsAsync()).Should().BeEquivalentTo(ExpectedMigrations);
        // IEntityType.Name is namespace-qualified; the inventory above is written as CLR type
        // names, which is what makes an unexpected entity type readable in a failure message.
        var entityTypeNames = context.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();
        entityTypeNames.Should().BeEquivalentTo(ExpectedEntityTypeNames);

        var state = await ReadFoundationState(connectionString);
        state.SchemaExists.Should().BeTrue();
        state.Tables.Should().BeEquivalentTo(
            ExpectedTables.Append(DatabaseConventions.MigrationsHistoryTable));

        await VerifyIdentityBootstrapAsync(connectionString);
        await VerifyClientWebsiteRegistryAsync(connectionString);
        await VerifyEnvironmentEndpointRegistryAsync(connectionString);
        await VerifyMonitoringExecutionFoundationAsync(connectionString);
        await VerifyHttpMonitoringHistoryAsync(connectionString);
        await VerifyLogicalCheckExecutionAsync(connectionString);
        await VerifyHealthConfirmationAsync(connectionString);
        await VerifyHangfireSchedulingAsync(connectionString);
        await VerifyManualChecksAndHistoryAsync(connectionString);
        await VerifyManualChecksUnavailableWhenSchedulingDisabledAsync(connectionString);
        await VerifyHealthMaintenanceAndIncidentsAsync(connectionString);
        await VerifyDurableNotificationsAsync(connectionString);
        await VerifyCompetingFinalizationOpensExactlyOneIncidentAsync(connectionString);
        await VerifyDistinctIssueKeysCreateDistinctIncidentsAsync(connectionString);
        await VerifyReminderEscalationSweepBoundariesAsync(connectionString);
        await VerifyMaintenanceClassifiedResultRetentionAsync(connectionString);
        await VerifyRecurringMaintenanceExpansionAsync(connectionString);
        await VerifySeoObservationContractAsync(connectionString);
        await VerifySslCertificateMonitoringAsync(connectionString);
        await VerifyCrawlResultContractAsync(connectionString);
        await ReportingQueryCoreAssertions.VerifyAsync(connectionString);
        await VerifyEndpointPurgeRemovesEveryReferenceAsync(connectionString);
        await VerifyPhaseThreeToPhaseFourUpgradeAsync(connectionString);
        await VerifyPhaseTwoUpgradeAsync(connectionString);
        await VerifyPhaseOneUpgradeAndRepeatabilityAsync(connectionString);
    }

    /// <summary>
    /// Certificate monitors follow the endpoint's scheme (BR-C01), and the fingerprint written
    /// by the migration's SQL backfill has to be byte-identical to the one the application
    /// computes — dispatch rejects any check whose stored fingerprint does not recompute, so a
    /// mismatch would silently disable every backfilled certificate monitor.
    /// </summary>
    private static async Task VerifySslCertificateMonitoringAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString,
            // BR-C07's urgent re-check is gated on scheduling being enabled, and the option
            // defaults to off, so the recheck below never queues anything without this.
            ["Monitoring:Scheduling:Enabled"] = "true"
        }).Build();
        await using var services = new ServiceCollection().AddLogging()
            .AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var endpointService = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();
        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var access = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        // An unordered First is whatever row the planner hands back, and the plaintext endpoint
        // below is rejected in a production environment for want of an HTTP exception reason, so
        // this stage passed or failed from one run to the next on row order alone.
        var environmentId = await database.Environments
            .Where(candidate => candidate.DeletedAt == null
                && candidate.IsActive
                && !candidate.IsProduction
                && candidate.Website.DeletedAt == null)
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => candidate.Id).FirstAsync();

        var httpsResult = await endpointService.CreateAsync(
            new(environmentId, "https://certificates.test/status", null, true, null,
                TargetAuthorizationKinds.Owned, "Certificate fixture owned by the project.", null),
            access);
        httpsResult.Succeeded.Should().BeTrue(string.Join(" ", httpsResult.Errors));
        var httpsEndpointId = httpsResult.EntityId!.Value;

        var sslMonitor = await database.EndpointMonitors.SingleAsync(monitor =>
            monitor.EndpointId == httpsEndpointId
            && monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType
            && monitor.DeletedAt == null);
        sslMonitor.IntervalSeconds.Should().Be(RegistryDefaults.SslIntervalSeconds);
        sslMonitor.PolicyProfileId.Should().Be(RegistryDefaults.SslCertificatePolicyProfileId);

        var httpResult = await endpointService.CreateAsync(
            new(environmentId, "http://plaintext.test/status", null, true, null,
                TargetAuthorizationKinds.Owned, "Certificate fixture owned by the project.", null),
            access);
        httpResult.Succeeded.Should().BeTrue(string.Join(" ", httpResult.Errors));
        (await database.EndpointMonitors.AnyAsync(monitor =>
            monitor.EndpointId == httpResult.EntityId!.Value
            && monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType))
            .Should().BeFalse("an HTTP endpoint presents no certificate to monitor");

        await VerifyBackfilledFingerprintMatchesApplicationAsync(connectionString, sslMonitor.Id);
        await VerifyUrgentCertificateRecheckAsync(
            scope, database, sslMonitor.Id, httpsEndpointId, "https://certificates.test/status");
        await VerifyCertificateMonitorFollowsTlsIdentityAsync(
            database, endpointService, access, httpsEndpointId, sslMonitor.Id);
    }

    /// <summary>
    /// A different host or port is a different certificate, so the monitor is retired and
    /// replaced rather than inheriting the previous host's observations. This also proves the
    /// retire and the replacement can be written in one save without tripping the partial
    /// unique index on (endpoint_id, monitor_type) for live monitors.
    /// </summary>
    private static async Task VerifyCertificateMonitorFollowsTlsIdentityAsync(
        ApplicationDbContext database,
        IEndpointRegistryService endpointService,
        RegistryAccessContext access,
        Guid endpointId,
        Guid originalSslMonitorId)
    {
        database.ChangeTracker.Clear();
        var endpoint = await database.Endpoints.SingleAsync(candidate => candidate.Id == endpointId);
        var renamed = await endpointService.UpdateAsync(
            new(endpointId, "https://renamed-certificates.test/status", null, true, null,
                TargetAuthorizationKinds.Owned, "Certificate fixture owned by the project.", null,
                endpoint.Version),
            access);
        renamed.Succeeded.Should().BeTrue(string.Join(" ", renamed.Errors));

        database.ChangeTracker.Clear();
        var monitors = await database.EndpointMonitors.AsNoTracking()
            .Where(monitor => monitor.EndpointId == endpointId
                && monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType)
            .ToArrayAsync();
        monitors.Should().HaveCount(2);
        monitors.Single(monitor => monitor.Id == originalSslMonitorId).DeletedAt.Should().NotBeNull();
        var replacement = monitors.Single(monitor => monitor.Id != originalSslMonitorId);
        replacement.DeletedAt.Should().BeNull();
        replacement.ConfigurationFingerprint.Should().Be(
            RegistryDefaults.CreateSslFingerprint("https://renamed-certificates.test/status", false));

        // A same-identity edit keeps the monitor, so certificate history is not reset by an
        // unrelated change.
        database.ChangeTracker.Clear();
        endpoint = await database.Endpoints.SingleAsync(candidate => candidate.Id == endpointId);
        (await endpointService.UpdateAsync(
            new(endpointId, "https://renamed-certificates.test/other", null, true, null,
                TargetAuthorizationKinds.Owned, "Certificate fixture owned by the project.", null,
                endpoint.Version),
            access))
            .Succeeded.Should().BeTrue();
        (await database.EndpointMonitors.AsNoTracking().CountAsync(monitor =>
            monitor.EndpointId == endpointId
            && monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType
            && monitor.DeletedAt == null))
            .Should().Be(1);
    }

    /// <summary>
    /// BR-C07: a TLS-related availability failure queues an out-of-band certificate check, but
    /// a flapping host must not be able to queue one per failed check.
    /// </summary>
    private static async Task VerifyUrgentCertificateRecheckAsync(
        AsyncServiceScope scope,
        ApplicationDbContext database,
        Guid sslMonitorId,
        Guid endpointId,
        string url)
    {
        var scheduler = scope.ServiceProvider.GetRequiredService<ISslUrgentCheckScheduler>();
        var request = new SafeHttpTransportRequest(endpointId, url, true);
        var tlsFailure = new HttpTransportEvidence(request, TransportFailure(SafeHttpFailureKind.Tls));

        (await PrepareUrgentAsync(database, scheduler, endpointId, tlsFailure)).Should().NotBeNull();
        var urgent = await database.LogicalChecks.AsNoTracking()
            .Where(check => check.EndpointMonitorId == sslMonitorId
                && check.Source == LogicalCheckSources.Urgent)
            .ToArrayAsync();
        urgent.Should().ContainSingle("a TLS failure triggers an immediate certificate check");
        (await database.DurableWork.AsNoTracking()
            .Where(work => work.LogicalCheckId == urgent[0].Id)
            .Select(work => work.WorkKind)
            .SingleAsync())
            .Should().Be(DurableWorkKinds.SslCheck);

        (await PrepareUrgentAsync(database, scheduler, endpointId, tlsFailure))
            .Should().BeNull("the per-endpoint cooldown suppresses a second urgent check");
        (await database.LogicalChecks.AsNoTracking().CountAsync(check =>
            check.EndpointMonitorId == sslMonitorId && check.Source == LogicalCheckSources.Urgent))
            .Should().Be(1);

        // A non-TLS failure is not evidence about the certificate.
        (await PrepareUrgentAsync(
            database, scheduler, endpointId,
            new HttpTransportEvidence(request, TransportFailure(SafeHttpFailureKind.Timeout))))
            .Should().BeNull();
        (await database.LogicalChecks.AsNoTracking().CountAsync(check =>
            check.EndpointMonitorId == sslMonitorId && check.Source == LogicalCheckSources.Urgent))
            .Should().Be(1);
    }

    /// <summary>
    /// The scheduler writes into its caller's transaction, so the test supplies one exactly as
    /// finalization does.
    /// </summary>
    private static async Task<UrgentCertificateCheck?> PrepareUrgentAsync(
        ApplicationDbContext database,
        ISslUrgentCheckScheduler scheduler,
        Guid endpointId,
        LogicalCheckTerminalEvidence evidence)
    {
        await using var transaction = await database.Database.BeginTransactionAsync();
        var prepared = await scheduler.PrepareAfterTlsFailureAsync(
            endpointId, evidence, DateTimeOffset.UtcNow);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return prepared;
    }

    private static SafeHttpTransportResult TransportFailure(SafeHttpFailureKind failure) => new(
        failure, null, null, TimeSpan.FromSeconds(1), 0, false,
        ReadOnlyMemory<byte>.Empty, [], null, null);

    private static async Task VerifyBackfilledFingerprintMatchesApplicationAsync(
        string connectionString,
        Guid sslMonitorId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT
                endpoint.normalized_url,
                environment.is_production,
                monitor.configuration_fingerprint,
                encode(sha256(convert_to(
                    'v2|'
                    || octet_length(endpoint.normalized_url)::text || ':'
                    || endpoint.normalized_url || '|'
                    || '14:SslCertificate|'
                    || '1:' || CASE WHEN environment.is_production THEN '1' ELSE '0' END || '|'
                    || '5:86400|2:15|1:1|1:1|-1:|-1:|0:|-1:|'
                    || '17:OrdinalIgnoreCase|7:Warning|7:2097152|2:10|',
                    'UTF8')), 'hex')
            FROM web_health.endpoint_monitor AS monitor
            JOIN web_health.endpoint AS endpoint ON endpoint.id = monitor.endpoint_id
            JOIN web_health.environment AS environment ON environment.id = endpoint.environment_id
            WHERE monitor.id = @id
            """, connection);
        command.Parameters.AddWithValue("id", sslMonitorId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        var normalizedUrl = reader.GetString(0);
        var isProduction = reader.GetBoolean(1);
        var storedFingerprint = reader.GetString(2);
        var migrationFingerprint = reader.GetString(3);

        // All three must agree: what the application stored, what the migration's SQL would
        // compute for the same endpoint, and what the fingerprint function produces today.
        var applicationFingerprint = RegistryDefaults.CreateSslFingerprint(normalizedUrl, isProduction);
        storedFingerprint.Should().Be(applicationFingerprint);
        migrationFingerprint.Should().Be(applicationFingerprint);
    }

    /// <summary>
    /// These scenarios were written when an endpoint had exactly one monitor. HTTPS endpoints
    /// now also carry a certificate monitor, so each query states the availability intent it
    /// always had rather than relying on there being only one monitor to find.
    /// </summary>
    private static IQueryable<EndpointMonitor> AvailabilityMonitors(ApplicationDbContext database) =>
        database.EndpointMonitors.Where(monitor =>
            monitor.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType);

    /// <summary>
    /// An availability monitor the calling stage owns, found by the URL it names rather than by
    /// position. Picking an existing monitor by ordinal couples a stage to whatever every earlier
    /// stage happened to leave on it — a held lease, issue state, an open incident — and silently
    /// selects a different monitor as soon as the surrounding fixtures shift. The endpoint is
    /// plain HTTP in a non-production environment so it carries no certificate monitor to retire
    /// and raises no HTTPS-required finding.
    /// </summary>
    /// <summary>
    /// The same owned fixture for a stage that drives its own <see cref="ApplicationDbContext"/>
    /// instances rather than resolving one from a scope, as the concurrency stages do.
    /// </summary>
    private static async Task<Guid> CreateOwnedMonitorIdAsync(string connectionString, string url)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging()
            .AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await CreateOwnedMonitorAsync(scope, database, url)).Id;
    }

    private static async Task<EndpointMonitor> CreateOwnedMonitorAsync(
        AsyncServiceScope scope,
        ApplicationDbContext database,
        string url)
    {
        var endpointService = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();
        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var access = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var environmentId = await database.Environments
            .Where(candidate => candidate.DeletedAt == null
                && candidate.IsActive
                && !candidate.IsProduction
                && candidate.Website.DeletedAt == null)
            .OrderBy(candidate => candidate.CreatedAt).ThenBy(candidate => candidate.Id)
            .Select(candidate => candidate.Id)
            .FirstAsync();

        var created = await endpointService.CreateAsync(
            new(environmentId, url, null, true, null,
                TargetAuthorizationKinds.Owned, "Foundation fixture owned by the project.", null),
            access);
        created.Succeeded.Should().BeTrue(string.Join(" ", created.Errors));

        database.ChangeTracker.Clear();
        return await AvailabilityMonitors(database)
            .Include(monitor => monitor.Endpoint).ThenInclude(endpoint => endpoint.Environment)
            .SingleAsync(monitor => monitor.EndpointId == created.EntityId!.Value
                && monitor.DeletedAt == null);
    }

    private static async Task VerifyEnvironmentEndpointRegistryAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var environmentService = scope.ServiceProvider.GetRequiredService<IEnvironmentRegistryService>();
        var endpointService = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();
        var targetReader = scope.ServiceProvider.GetRequiredService<ITargetRegistryReader>();
        var targetAuthorization = scope.ServiceProvider.GetRequiredService<ITargetAuthorizationService>();
        var monitoringEligibility = scope.ServiceProvider.GetRequiredService<IMonitoringEligibilityService>();

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var developer = await database.Users.SingleAsync(user => user.Email == "registry-developer@example.test");
        var viewer = await database.Users.SingleAsync(user => user.Email == "registry-viewer@example.test");
        var developerOwnerId = await database.OwnerSubjects.Where(owner => owner.UserId == developer.Id)
            .Select(owner => owner.Id).SingleAsync();
        var administratorOwnerId = await database.OwnerSubjects.Where(owner => owner.UserId == administrator.Id)
            .Select(owner => owner.Id).SingleAsync();
        var website = await database.Websites.SingleAsync(candidate =>
            candidate.Client.Name == "Second Client" && candidate.Name == "Portal");
        var administratorAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var operationsAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Operations]);

        var stagingResult = await environmentService.CreateAsync(
            new(website.Id, "  Staging  ", EnvironmentTypes.Staging, "HTTPS://Example.test:443/base", true),
            administratorAccess);
        stagingResult.Succeeded.Should().BeTrue(string.Join(" ", stagingResult.Errors));
        var stagingId = stagingResult.EntityId ?? throw new InvalidOperationException("Environment id was not returned.");
        (await environmentService.CreateAsync(
            new(website.Id, "staging", EnvironmentTypes.Test, null, true), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        staging.Name.Should().Be("Staging");
        staging.BaseUrl.Should().Be("https://example.test/base");
        staging.IsProduction.Should().BeFalse();
        website = await database.Websites.SingleAsync(candidate => candidate.Id == website.Id);
        website.IsEnabled = true;
        await database.SaveChangesAsync();

        var endpointResult = await endpointService.CreateAsync(
            new(stagingId, " HTTPS://EXAMPLE.test:443/a/../Health?q=%41 ", developerOwnerId, true, null,
                TargetAuthorizationKinds.Owned, "Integration fixture owned by the project.", null),
            administratorAccess);
        endpointResult.Succeeded.Should().BeTrue(string.Join(" ", endpointResult.Errors));
        var endpointId = endpointResult.EntityId ?? throw new InvalidOperationException("Endpoint id was not returned.");
        (await endpointService.CreateAsync(
            new(stagingId, "https://example.test/Health?q=A", null, true, null,
                TargetAuthorizationKinds.Owned, "Integration fixture owned by the project.", null), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var endpoint = await database.Endpoints.Include(candidate => candidate.Monitors)
            .SingleAsync(candidate => candidate.Id == endpointId);
        endpoint.NormalizedUrl.Should().Be("https://example.test/Health?q=A");
        endpoint.NormalizedUrlHash.Should().HaveCount(32);
        // An HTTPS endpoint carries both an availability monitor and a certificate monitor.
        endpoint.Monitors.Select(candidate => candidate.MonitorType).Should()
            .BeEquivalentTo(["HttpAvailability", "SslCertificate"]);
        var monitor = endpoint.Monitors.Single(candidate =>
            candidate.MonitorType == "HttpAvailability");
        monitor.ScheduleAnchor.Should().Be(monitor.CreatedAt);
        monitor.NextDueAt.Should().Be(monitor.CreatedAt);
        var authorizationEvidence = await database.TargetAuthorizations.SingleAsync(
            evidence => evidence.EndpointId == endpointId && evidence.RevokedAt == null);
        authorizationEvidence.AuthorizationKind.Should().Be(TargetAuthorizationKinds.Owned);
        authorizationEvidence.NormalizedHost.Should().Be("example.test");
        authorizationEvidence.Port.Should().Be(443);

        (await endpointService.CreateAsync(
            new(stagingId, "/relative", null, true, null, null, null, null), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        (await endpointService.CreateAsync(
            new(stagingId, "https://no-evidence.example.test/", null, true, null, null, null, null),
            administratorAccess)).Status.Should().Be(RegistryMutationStatus.ValidationFailed);

        var productionResult = await environmentService.CreateAsync(
            new(website.Id, "Production", EnvironmentTypes.Production, "https://example.test", true),
            administratorAccess);
        productionResult.Succeeded.Should().BeTrue(string.Join(" ", productionResult.Errors));
        var productionId = productionResult.EntityId ?? throw new InvalidOperationException("Production environment id was not returned.");
        (await endpointService.CreateAsync(
            new(productionId, "http://legacy.example.test/", null, true, "Legacy appliance",
                TargetAuthorizationKinds.ExplicitPermission, "Permission ticket TEST-1", null), operationsAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var productionHttp = await endpointService.CreateAsync(
            new(productionId, "http://legacy.example.test/", null, true,
                "Legacy appliance requires HTTP during migration.", TargetAuthorizationKinds.ExplicitPermission,
                "Permission ticket TEST-2", null),
            administratorAccess);
        productionHttp.Succeeded.Should().BeTrue(string.Join(" ", productionHttp.Errors));

        var stagingHttp = await endpointService.CreateAsync(
            new(stagingId, "http://staging.example.test/", null, false, null, null, null, null),
            administratorAccess);
        stagingHttp.Succeeded.Should().BeTrue(string.Join(" ", stagingHttp.Errors));
        database.ChangeTracker.Clear();
        staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        (await environmentService.UpdateAsync(
            new(staging.Id, staging.Name, EnvironmentTypes.Production, staging.BaseUrl, true, staging.Version), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);

        database.AccessGrants.Add(new AccessGrant
        {
            Id = Guid.NewGuid(),
            UserId = viewer.Id,
            AccessLevel = "Read",
            EndpointId = endpointId,
            EffectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = administrator.Id
        });
        await database.SaveChangesAsync();
        (await targetReader.ListEndpointsAsync(stagingId, new(viewer.Id, [ApplicationRoles.Viewer])))
            .Should().ContainSingle(item => item.Id == endpointId);
        var eligibilityState = await database.Endpoints.AsNoTracking()
            .Where(candidate => candidate.Id == endpointId)
            .Select(candidate => new
            {
                EndpointActive = candidate.DeletedAt == null && candidate.IsEnabled,
                EnvironmentActive = candidate.Environment.DeletedAt == null && candidate.Environment.IsActive,
                WebsiteActive = candidate.Environment.Website.DeletedAt == null && candidate.Environment.Website.IsEnabled,
                ClientActive = candidate.Environment.Website.Client.DeletedAt == null
                    && candidate.Environment.Website.Client.IsActive,
                MonitorActive = candidate.Monitors.Any(monitor => monitor.DeletedAt == null && monitor.IsEnabled),
                EvidenceActive = candidate.TargetAuthorizations.Any(evidence =>
                    evidence.RevokedAt == null
                    && evidence.EffectiveFrom <= DateTimeOffset.UtcNow
                    && (evidence.ExpiresAt == null || evidence.ExpiresAt > DateTimeOffset.UtcNow)
                    && evidence.NormalizedHost == candidate.NormalizedHost
                    && evidence.Port == candidate.EffectivePort)
            }).SingleAsync();
        eligibilityState.EndpointActive.Should().BeTrue();
        eligibilityState.EnvironmentActive.Should().BeTrue();
        eligibilityState.WebsiteActive.Should().BeTrue();
        eligibilityState.ClientActive.Should().BeTrue();
        eligibilityState.MonitorActive.Should().BeTrue();
        eligibilityState.EvidenceActive.Should().BeTrue();
        (await monitoringEligibility.IsEndpointEligibleAsync(endpointId)).Should().BeTrue();
        (await targetReader.ListEndpointsAsync(
            stagingId, new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().Contain(item => item.Id == endpointId);
        (await targetAuthorization.CanTestEndpointAsync(endpointId, new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().BeTrue();
        (await targetAuthorization.CanTestEndpointAsync(endpointId, new(viewer.Id, [ApplicationRoles.Viewer])))
            .Should().BeFalse();

        website = await database.Websites.SingleAsync(candidate => candidate.Id == website.Id);
        website.IsEnabled = false;
        website.Version++;
        await database.SaveChangesAsync();
        (await monitoringEligibility.IsEndpointEligibleAsync(endpointId)).Should().BeFalse();
        (await AvailabilityMonitors(database).Where(candidate => candidate.EndpointId == endpointId)
            .Select(candidate => candidate.IsEnabled).SingleAsync()).Should().BeTrue();
        website.IsEnabled = true;
        website.Version++;
        await database.SaveChangesAsync();
        (await monitoringEligibility.IsEndpointEligibleAsync(endpointId)).Should().BeTrue();

        var client = await database.Clients.SingleAsync(candidate => candidate.Id == website.ClientId);
        client.IsActive = false;
        client.Version++;
        await database.SaveChangesAsync();
        (await monitoringEligibility.IsEndpointEligibleAsync(endpointId)).Should().BeFalse();
        client.IsActive = true;
        client.Version++;
        await database.SaveChangesAsync();
        (await monitoringEligibility.IsEndpointEligibleAsync(endpointId)).Should().BeTrue();

        website = await database.Websites.SingleAsync(candidate => candidate.Id == website.Id);
        website.OwnerSubjectId = developerOwnerId;
        website.Version++;
        await database.SaveChangesAsync();
        var overriddenEndpoint = await endpointService.CreateAsync(
            new(stagingId, "https://override.example.test/", administratorOwnerId, true, null,
                TargetAuthorizationKinds.Owned, "Administrator-owned integration fixture.", null, 7),
            administratorAccess);
        overriddenEndpoint.Succeeded.Should().BeTrue(string.Join(" ", overriddenEndpoint.Errors));
        var overriddenEndpointId = overriddenEndpoint.EntityId!.Value;
        var overriddenMonitor = await AvailabilityMonitors(database).AsNoTracking()
            .SingleAsync(candidate => candidate.EndpointId == overriddenEndpointId);
        overriddenMonitor.IntervalSeconds.Should().Be(420);
        MonitorIntervalOverride.GetSeconds(overriddenMonitor.BoundedOverrides).Should().Be(420);
        (await endpointService.CreateAsync(
            new(stagingId, "https://operations-override.example.test/", null, false, null,
                null, null, null, 7), operationsAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        (await targetReader.ListEndpointsAsync(stagingId,
            new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().NotContain(item => item.Id == overriddenEndpointId);
        (await targetAuthorization.CanTestEndpointAsync(
            overriddenEndpointId, new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().BeFalse();

        var staleEndpoint = await endpointService.UpdateAsync(
            new(endpointId, endpoint.DisplayUrl, endpoint.OwnerSubjectId, endpoint.IsEnabled, null,
                TargetAuthorizationKinds.Owned, "Integration fixture owned by the project.", null, 0),
            administratorAccess);
        staleEndpoint.Status.Should().Be(RegistryMutationStatus.ConcurrencyConflict);

        var staleEnvironment = await environmentService.UpdateAsync(
            new(staging.Id, staging.Name, staging.EnvironmentType, staging.BaseUrl, staging.IsActive, 0),
            administratorAccess);
        staleEnvironment.Status.Should().Be(RegistryMutationStatus.ConcurrencyConflict);

        await VerifyEnvironmentTypeConstraintAsync(connectionString, stagingId);
        await VerifyProductionHttpConstraintAsync(connectionString, productionId, developer.Id, administrator.Id);
        await VerifyProductionHttpTransitionsAsync(
            connectionString, website.Id, productionId, developer.Id, administrator.Id);
        await VerifyMonitorPolicyConstraintAsync(connectionString, endpointId, administrator.Id);

        database.ChangeTracker.Clear();
        endpoint = await database.Endpoints.SingleAsync(candidate => candidate.Id == endpointId);
        var updateEndpoint = await endpointService.UpdateAsync(
            new(endpoint.Id, "https://example.test/health-v2", endpoint.OwnerSubjectId, true, null,
                TargetAuthorizationKinds.Owned, "Updated integration fixture ownership evidence.", null, endpoint.Version),
            administratorAccess);
        updateEndpoint.Succeeded.Should().BeTrue(string.Join(" ", updateEndpoint.Errors));
        endpoint = await database.Endpoints.SingleAsync(candidate => candidate.Id == endpointId);
        (await endpointService.DisableAsync(new(endpoint.Id, endpoint.Version), administratorAccess)).Succeeded.Should().BeTrue();
        endpoint = await database.Endpoints.SingleAsync(candidate => candidate.Id == endpointId);
        (await endpointService.DeleteAsync(new(endpoint.Id, endpoint.Version), administratorAccess)).Succeeded.Should().BeTrue();
        (await targetReader.ListEndpointsAsync(stagingId, administratorAccess)).Should().NotContain(item => item.Id == endpointId);
        (await targetReader.ListDeletedEndpointsAsync(administratorAccess)).Should().Contain(item => item.Id == endpointId);
        endpoint = await database.Endpoints.SingleAsync(candidate => candidate.Id == endpointId);
        (await endpointService.RestoreAsync(new(endpoint.Id, endpoint.Version), administratorAccess)).Succeeded.Should().BeTrue();

        database.ChangeTracker.Clear();
        staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        var renamedMonitor = await AvailabilityMonitors(database).SingleAsync(candidate => candidate.EndpointId == endpointId);
        var overdueSlot = DateTimeOffset.UtcNow.AddMinutes(-30);
        renamedMonitor.NextDueAt = overdueSlot;
        var intervalBeforeRename = renamedMonitor.IntervalSeconds;
        var versionBeforeRename = renamedMonitor.Version;
        await database.SaveChangesAsync();
        (await environmentService.UpdateAsync(
            new(staging.Id, "Staging Updated", EnvironmentTypes.Staging, staging.BaseUrl, true, staging.Version),
            administratorAccess)).Succeeded.Should().BeTrue();
        var monitorAfterRename = await AvailabilityMonitors(database).AsNoTracking()
            .SingleAsync(candidate => candidate.Id == renamedMonitor.Id);
        monitorAfterRename.NextDueAt.Should().BeCloseTo(overdueSlot, TimeSpan.FromMilliseconds(1));
        monitorAfterRename.IntervalSeconds.Should().Be(intervalBeforeRename);
        monitorAfterRename.Version.Should().Be(versionBeforeRename);
        staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        (await environmentService.DisableAsync(new(staging.Id, staging.Version), administratorAccess)).Succeeded.Should().BeTrue();
        (await targetAuthorization.CanTestEndpointAsync(endpointId,
            new(developer.Id, [ApplicationRoles.DeveloperSupport]))).Should().BeFalse();
        (await AvailabilityMonitors(database).Where(candidate => candidate.EndpointId == endpointId)
            .Select(candidate => candidate.IsEnabled).SingleAsync()).Should().BeTrue();
        staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        (await environmentService.DeleteAsync(new(staging.Id, staging.Version), administratorAccess)).Succeeded.Should().BeTrue();
        (await targetReader.ListDeletedEnvironmentsAsync(administratorAccess)).Should().Contain(item => item.Id == stagingId);
        staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        (await environmentService.RestoreAsync(new(staging.Id, staging.Version), administratorAccess)).Succeeded.Should().BeTrue();

        var actions = await database.AuditEvents.Where(audit =>
                audit.EntityIdentifier == endpointId.ToString() || audit.EntityIdentifier == stagingId.ToString())
            .Select(audit => audit.Action).ToListAsync();
        actions.Should().Contain(["environment.created", "environment.updated", "environment.disabled", "environment.deleted", "environment.restored"]);
        actions.Should().Contain(["endpoint.created", "endpoint.updated", "endpoint.disabled", "endpoint.deleted", "endpoint.restored"]);

        var endpointAuditPayloads = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == endpointId.ToString())
            .Select(audit => new { audit.BeforeValues, audit.AfterValues })
            .ToListAsync();
        endpointAuditPayloads.Should().OnlyContain(payload =>
            !(payload.BeforeValues ?? string.Empty).Contains("?q=", StringComparison.Ordinal)
            && !(payload.AfterValues ?? string.Empty).Contains("?q=", StringComparison.Ordinal)
            && !(payload.BeforeValues ?? string.Empty).Contains("Legacy appliance", StringComparison.Ordinal)
            && !(payload.AfterValues ?? string.Empty).Contains("Legacy appliance", StringComparison.Ordinal)
            && !(payload.BeforeValues ?? string.Empty).Contains("Integration fixture owned", StringComparison.Ordinal)
            && !(payload.AfterValues ?? string.Empty).Contains("Integration fixture owned", StringComparison.Ordinal)
            && !(payload.BeforeValues ?? string.Empty).Contains("Updated integration fixture", StringComparison.Ordinal)
            && !(payload.AfterValues ?? string.Empty).Contains("Updated integration fixture", StringComparison.Ordinal));

    }

    private static async Task VerifyMonitoringExecutionFoundationAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var leaseService = scope.ServiceProvider.GetRequiredService<IExecutionLeaseService>();
        var monitor = await AvailabilityMonitors(database)
            .Include(candidate => candidate.Endpoint)
                .ThenInclude(endpoint => endpoint.Environment)
                    .ThenInclude(environment => environment.Website)
                        .ThenInclude(website => website.Client)
            .Include(candidate => candidate.Endpoint)
                .ThenInclude(endpoint => endpoint.TargetAuthorizations)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstAsync();
        monitor.ConfigurationFingerprint.Should().Be(HttpPolicyFingerprint.Create(new(
            monitor.Endpoint.NormalizedUrl,
            monitor.MonitorType,
            monitor.Endpoint.Environment.IsProduction,
            monitor.IntervalSeconds,
            monitor.TimeoutSeconds,
            monitor.FailureConfirmationCount,
            monitor.RecoveryConfirmationCount,
            monitor.WarningThresholdMs,
            monitor.CriticalThresholdMs,
            [],
            null,
            "OrdinalIgnoreCase",
            FindingSeverities.Warning,
            SafeHttpTransportDefaults.MaxDecodedBodyBytes,
            SafeHttpTransportDefaults.MaxRedirects)));
        var scheduledFor = monitor.NextDueAt;
        var logicalCheckId = Guid.NewGuid();
        var cadenceKey = MonitorCadence.CreateCadenceKey(monitor.Id, scheduledFor);

        database.LogicalChecks.Add(new LogicalCheck
        {
            Id = logicalCheckId,
            EndpointMonitorId = monitor.Id,
            Source = LogicalCheckSources.Scheduled,
            ScheduledFor = scheduledFor,
            State = LogicalCheckStates.Pending,
            CadenceKey = cadenceKey,
            PolicyFingerprint = monitor.ConfigurationFingerprint,
            CreatedAt = scheduledFor
        });
        database.CheckConfigurationSnapshots.Add(new CheckConfigurationSnapshot
        {
            LogicalCheckId = logicalCheckId,
            SchemaVersion = 1,
            MonitorType = monitor.MonitorType,
            ConfigurationFingerprint = monitor.ConfigurationFingerprint,
            IntervalSeconds = monitor.IntervalSeconds,
            TimeoutSeconds = monitor.TimeoutSeconds,
            FailureConfirmationCount = monitor.FailureConfirmationCount,
            RecoveryConfirmationCount = monitor.RecoveryConfirmationCount,
            WarningThresholdMs = monitor.WarningThresholdMs,
            CriticalThresholdMs = monitor.CriticalThresholdMs,
            IntervalSource = ConfigurationValueSources.EnvironmentDefault,
            TimeoutSource = ConfigurationValueSources.PolicyProfile,
            ConfirmationSource = ConfigurationValueSources.PolicyProfile,
            ThresholdSource = ConfigurationValueSources.PolicyProfile,
            CreatedAt = scheduledFor
        });
        database.DurableWork.Add(new DurableWork
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = logicalCheckId,
            WorkKind = "HttpCheck",
            DedupeKey = $"v1|{logicalCheckId:N}|http-check",
            QueueName = "monitoring",
            State = DurableWorkStates.Pending,
            AvailableAt = scheduledFor,
            CreatedAt = scheduledFor,
            UpdatedAt = scheduledFor
        });
        await database.SaveChangesAsync();

        var queuedCheck = await database.LogicalChecks.SingleAsync(check => check.Id == logicalCheckId);
        queuedCheck.State = LogicalCheckStates.Queued;
        queuedCheck.QueuedAt = scheduledFor;
        await database.SaveChangesAsync();

        await VerifyDuplicateCadenceRejectedAsync(
            connectionString,
            monitor.Id,
            monitor.ConfigurationFingerprint,
            scheduledFor,
            cadenceKey);
        await VerifySnapshotIsImmutableAsync(connectionString, logicalCheckId);
        await VerifyMissingSnapshotRejectedAsync(
            connectionString, monitor.Id, monitor.ConfigurationFingerprint, scheduledFor);
        await VerifySystemUrgentCheckAsync(
            connectionString, monitor.Id, monitor.ConfigurationFingerprint, scheduledFor);
        await VerifyNegativeThresholdsRejectedAsync(
            connectionString, monitor.Id, monitor.ConfigurationFingerprint, scheduledFor);

        var invalidLease = async () => await leaseService.TryAcquireAsync(new(
            monitor.Id,
            logicalCheckId,
            Guid.NewGuid(),
            TimeSpan.FromMinutes(16)));
        await invalidLease.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var otherMonitorId = await AvailabilityMonitors(database)
            .Where(candidate => candidate.Id != monitor.Id)
            .OrderBy(candidate => candidate.CreatedAt).ThenBy(candidate => candidate.Id)
            .Select(candidate => candidate.Id)
            .FirstAsync();
        var mismatchedLease = async () => await leaseService.TryAcquireAsync(new(
            otherMonitorId,
            logicalCheckId,
            Guid.NewGuid(),
            TimeSpan.FromMinutes(1)));
        var mismatch = await mismatchedLease.Should().ThrowAsync<PostgresException>();
        mismatch.Which.ConstraintName.Should().Be("fk_execution_lease_logical_check_monitor");

        await using var competingScope = services.CreateAsyncScope();
        var competingLeaseService = competingScope.ServiceProvider.GetRequiredService<IExecutionLeaseService>();
        var claims = await Task.WhenAll(
            leaseService.TryAcquireAsync(new(
                monitor.Id,
                logicalCheckId,
                Guid.NewGuid(),
                TimeSpan.FromMinutes(1))),
            competingLeaseService.TryAcquireAsync(new(
                monitor.Id,
                logicalCheckId,
                Guid.NewGuid(),
                TimeSpan.FromMinutes(1))));
        claims.Should().ContainSingle(claim => claim != null);
        var firstClaim = claims.Single(claim => claim != null)!;
        firstClaim.FencingGeneration.Should().Be(1);
        var persistedLease = await database.ExecutionLeases.AsNoTracking()
            .SingleAsync(lease => lease.EndpointMonitorId == monitor.Id);
        persistedLease.OwnerToken.Should().Be(firstClaim.OwnerToken);
        persistedLease.ExpiresAt.Should().BeAfter(persistedLease.AcquiredAt);
        (persistedLease.ExpiresAt - persistedLease.AcquiredAt).Should().Be(TimeSpan.FromMinutes(1));

        (await leaseService.ReleaseAsync(firstClaim)).Should().BeTrue();
        var recoveredClaim = await leaseService.TryAcquireAsync(new(
            monitor.Id,
            logicalCheckId,
            Guid.NewGuid(),
            TimeSpan.FromMinutes(1)));
        recoveredClaim.Should().NotBeNull();
        recoveredClaim!.FencingGeneration.Should().Be(2);
    }

    private static async Task VerifyHttpMonitoringHistoryAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var leaseService = scope.ServiceProvider.GetRequiredService<IExecutionLeaseService>();
        var finalizationService = scope.ServiceProvider.GetRequiredService<ILogicalCheckFinalizationService>();
        var monitor = await CreateOwnedMonitorAsync(scope, database, "http://http-monitoring-history.test/status");
        var now = DateTimeOffset.UtcNow;
        var logicalCheckId = Guid.NewGuid();
        var policyFingerprint = HttpPolicyFingerprint.Create(new(
            monitor.Endpoint.NormalizedUrl,
            monitor.MonitorType,
            monitor.Endpoint.Environment.IsProduction,
            monitor.IntervalSeconds,
            monitor.TimeoutSeconds,
            monitor.FailureConfirmationCount,
            monitor.RecoveryConfirmationCount,
            monitor.WarningThresholdMs,
            monitor.CriticalThresholdMs,
            [204, 404],
            "READY",
            "OrdinalIgnoreCase",
            FindingSeverities.Warning,
            8,
            10));
        var attemptId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        database.LogicalChecks.Add(new LogicalCheck
        {
            Id = logicalCheckId,
            EndpointMonitorId = monitor.Id,
            Source = LogicalCheckSources.Scheduled,
            ScheduledFor = now,
            State = LogicalCheckStates.Running,
            CadenceKey = MonitorCadence.CreateCadenceKey(monitor.Id, now),
            PolicyFingerprint = policyFingerprint,
            CreatedAt = now,
            QueuedAt = now,
            StartedAt = now
        });
        database.CheckConfigurationSnapshots.Add(new CheckConfigurationSnapshot
        {
            LogicalCheckId = logicalCheckId,
            SchemaVersion = 2,
            MonitorType = monitor.MonitorType,
            ConfigurationFingerprint = policyFingerprint,
            IntervalSeconds = monitor.IntervalSeconds,
            TimeoutSeconds = monitor.TimeoutSeconds,
            FailureConfirmationCount = monitor.FailureConfirmationCount,
            RecoveryConfirmationCount = monitor.RecoveryConfirmationCount,
            WarningThresholdMs = monitor.WarningThresholdMs,
            CriticalThresholdMs = monitor.CriticalThresholdMs,
            IntervalSource = ConfigurationValueSources.EnvironmentDefault,
            TimeoutSource = ConfigurationValueSources.PolicyProfile,
            ConfirmationSource = ConfigurationValueSources.PolicyProfile,
            ThresholdSource = ConfigurationValueSources.PolicyProfile,
            AcceptedStatusCodes = "204,404",
            RequiredContentMarker = "READY",
            ContentMarkerComparison = "OrdinalIgnoreCase",
            ProductionHttpSeverity = FindingSeverities.Warning,
            MaxResponseBodyBytes = 8,
            MaxRedirects = 10,
            CreatedAt = now
        });
        database.ExecutionAttempts.Add(new ExecutionAttempt
        {
            Id = attemptId,
            LogicalCheckId = logicalCheckId,
            AttemptNumber = 1,
            JobId = "history-test",
            WorkerId = "history-worker",
            StartedAt = now,
            InfrastructureOutcome = ExecutionAttemptOutcomes.Running
        });
        database.DurableWork.Add(new DurableWork
        {
            Id = workId,
            LogicalCheckId = logicalCheckId,
            WorkKind = DurableWorkKinds.HttpCheck,
            DedupeKey = $"v1|{logicalCheckId:N}|http-history",
            QueueName = "monitoring",
            State = DurableWorkStates.Processing,
            AvailableAt = now,
            AttemptCount = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        await database.SaveChangesAsync();

        var claim = await leaseService.TryAcquireAsync(new(
            monitor.Id,
            logicalCheckId,
            Guid.NewGuid(),
            TimeSpan.FromMinutes(1)));
        claim.Should().NotBeNull();
        var activeClaim = claim!;
        var endpointUri = new Uri(monitor.Endpoint.NormalizedUrl);
        var request = new SafeHttpTransportRequest(
            monitor.EndpointId,
            monitor.Endpoint.NormalizedUrl,
            false,
            MaxRedirects: 10,
            MaxResponseBodyBytes: 8,
            TimeoutSeconds: monitor.TimeoutSeconds);
        var transport = new SafeHttpTransportResult(
            null,
            200,
            new($"{endpointUri.Scheme}://{endpointUri.Authority}/landing"),
            TimeSpan.FromMilliseconds(42),
            7,
            false,
            "offline"u8.ToArray(),
            [new(
                302,
                endpointUri.GetLeftPart(UriPartial.Path),
                $"{endpointUri.Scheme}://{endpointUri.Authority}/landing",
                false)],
            SafeHttpRequestIdentity.Create(request));

        var otherRequest = request with
        {
            EndpointId = Guid.NewGuid(),
            Url = "https://other-endpoint.test/"
        };
        var otherResult = transport with { RequestIdentity = SafeHttpRequestIdentity.Create(otherRequest) };
        (await finalizationService.FinalizeAsync(new(
            activeClaim, attemptId, workId, new HttpTransportEvidence(request, otherResult))))
            .Should().Be(LogicalCheckFinalizationStatus.TargetMismatch);

        var policyMismatchRequest = request with { MaxRedirects = 9 };
        var policyMismatchResult = transport with
        {
            RequestIdentity = SafeHttpRequestIdentity.Create(policyMismatchRequest)
        };
        (await finalizationService.FinalizeAsync(new(
            activeClaim, attemptId, workId,
            new HttpTransportEvidence(policyMismatchRequest, policyMismatchResult))))
            .Should().Be(LogicalCheckFinalizationStatus.PolicyMismatch);

        var invalidResult = transport with
        {
            Redirects = [transport.Redirects[0] with { FromUrl = "https://other.test/" }]
        };
        (await finalizationService.FinalizeAsync(new(
            activeClaim, attemptId, workId, new HttpTransportEvidence(request, invalidResult))))
            .Should().Be(LogicalCheckFinalizationStatus.InvalidTransportResult);
        (await database.CheckResults.AnyAsync(result => result.LogicalCheckId == logicalCheckId))
            .Should().BeFalse();

        await using var competingScope = services.CreateAsyncScope();
        var competingFinalizationService = competingScope.ServiceProvider
            .GetRequiredService<ILogicalCheckFinalizationService>();
        var concurrentWrites = await Task.WhenAll(
            finalizationService.FinalizeAsync(new(
                activeClaim, attemptId, workId, new HttpTransportEvidence(request, transport))),
            competingFinalizationService.FinalizeAsync(new(
                activeClaim, attemptId, workId, new HttpTransportEvidence(request, transport))));
        concurrentWrites.Should().BeEquivalentTo([
            LogicalCheckFinalizationStatus.Finalized,
            LogicalCheckFinalizationStatus.AlreadyFinalized]);
        (await finalizationService.FinalizeAsync(new(
            activeClaim, attemptId, workId, new HttpTransportEvidence(request, transport))))
            .Should().Be(LogicalCheckFinalizationStatus.AlreadyFinalized);

        database.ChangeTracker.Clear();
        var result = await database.CheckResults.SingleAsync(candidate =>
            candidate.LogicalCheckId == logicalCheckId);
        result.Outcome.Should().Be(HttpResultOutcomes.Critical);
        result.FailureCategory.Should().Be(HttpFailureCategories.ContentMismatch);
        result.HttpStatus.Should().Be(200);
        result.TotalDurationMs.Should().Be(42);
        result.DecodedLength.Should().Be(7);
        result.CountsForUptime.Should().BeTrue();
        result.SafeDiagnostic.Should().NotContain("READY").And.NotContain("offline");
        (await database.RedirectHops.SingleAsync(hop => hop.LogicalCheckId == logicalCheckId))
            .HopNumber.Should().Be(1);
        var finding = await database.Findings.SingleAsync(candidate =>
            candidate.LogicalCheckId == logicalCheckId);
        finding.RuleKey.Should().Be("Http.ContentMismatch");
        finding.ObservedValue.Should().NotContain("offline").And.NotContain("READY");
        finding.ExpectedValue.Should().NotContain("offline").And.NotContain("READY");
        (await database.LogicalChecks.SingleAsync(check => check.Id == logicalCheckId))
            .State.Should().Be(LogicalCheckStates.Completed);

        await VerifyHttpHistoryConstraintsAsync(connectionString, logicalCheckId);
    }

    private static async Task VerifyHttpHistoryConstraintsAsync(
        string connectionString,
        Guid logicalCheckId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var duplicateResult = new NpgsqlCommand(
            "INSERT INTO web_health.check_result SELECT * FROM web_health.check_result WHERE logical_check_id = @id",
            connection))
        {
            duplicateResult.Parameters.AddWithValue("id", logicalCheckId);
            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => duplicateResult.ExecuteNonQueryAsync());
            exception.ConstraintName.Should().Be("pk_check_result");
        }

        const string orphanFindingSql = """
            INSERT INTO web_health.finding
                (id, logical_check_id, rule_key, severity, issue_key)
            VALUES (@id, @check_id, 'Http.Test', 'Critical', 'http:test');
            """;
        await using (var orphanFinding = new NpgsqlCommand(orphanFindingSql, connection))
        {
            orphanFinding.Parameters.AddWithValue("id", Guid.NewGuid());
            orphanFinding.Parameters.AddWithValue("check_id", Guid.NewGuid());
            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => orphanFinding.ExecuteNonQueryAsync());
            exception.ConstraintName.Should().Be("fk_finding_check_result_logical_check_id");
        }

        await using (var duplicateHop = new NpgsqlCommand(
            """
            INSERT INTO web_health.redirect_hop
                (id, logical_check_id, hop_number, normalized_from_url,
                 normalized_to_url, http_status, is_loop)
            SELECT @id, logical_check_id, hop_number, normalized_from_url,
                   normalized_to_url, http_status, is_loop
            FROM web_health.redirect_hop WHERE logical_check_id = @check_id;
            """,
            connection))
        {
            duplicateHop.Parameters.AddWithValue("id", Guid.NewGuid());
            duplicateHop.Parameters.AddWithValue("check_id", logicalCheckId);
            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => duplicateHop.ExecuteNonQueryAsync());
            exception.ConstraintName.Should().Be("ix_redirect_hop_logical_check_id_hop_number");
        }

        await using var bodyColumn = new NpgsqlCommand(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'web_health' AND table_name = 'check_result'
              AND column_name IN ('body', 'response_body', 'content');
            """,
            connection);
        Convert.ToInt32(await bodyColumn.ExecuteScalarAsync()).Should().Be(0);
    }

    private static async Task VerifyLogicalCheckExecutionAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var monitor = await CreateOwnedMonitorAsync(scope, database, "http://logical-check-execution.test/status");

        var successfulCheck = await CreateQueuedCheckAsync(database, monitor);
        var successfulHttpWork = successfulCheck.DurableWork.Single(work =>
            work.WorkKind == DurableWorkKinds.HttpCheck);
        var unrelatedWork = new DurableWork
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = successfulCheck.Id,
            WorkKind = "IncidentEvaluation",
            DedupeKey = $"v1|{successfulCheck.Id:N}|incident-evaluation",
            QueueName = "incidents",
            State = DurableWorkStates.Enqueued,
            AvailableAt = successfulCheck.CreatedAt,
            CreatedAt = successfulCheck.CreatedAt,
            UpdatedAt = successfulCheck.CreatedAt
        };
        database.DurableWork.Add(unrelatedWork);
        await database.SaveChangesAsync();
        var successfulTransport = new RecordingSafeHttpTransport(Success);
        var successfulExecution = CreateExecutionService(database, successfulTransport, true);
        (await successfulExecution.ExecuteAsync(new(
            successfulCheck.Id, successfulHttpWork.Id,
            "job-success", "worker-a")))
            .Should().Be(LogicalCheckExecutionStatus.Completed);
        (await successfulExecution.ExecuteAsync(new(
            successfulCheck.Id, successfulHttpWork.Id,
            "job-duplicate", "worker-b")))
            .Should().Be(LogicalCheckExecutionStatus.AlreadyCompleted);
        successfulTransport.CallCount.Should().Be(1);
        successfulTransport.LastRequest!.TimeoutSeconds.Should()
            .Be(successfulCheck.ConfigurationSnapshot.TimeoutSeconds);
        (await database.CheckResults.CountAsync(result => result.LogicalCheckId == successfulCheck.Id))
            .Should().Be(1);
        var successfulAttempts = await database.ExecutionAttempts
            .Where(attempt => attempt.LogicalCheckId == successfulCheck.Id)
            .ToArrayAsync();
        successfulAttempts.Should().ContainSingle();
        successfulAttempts[0].InfrastructureOutcome.Should().Be(ExecutionAttemptOutcomes.Succeeded);
        (await database.DurableWork.AsNoTracking().SingleAsync(work => work.Id == unrelatedWork.Id))
            .State.Should().Be(DurableWorkStates.Enqueued);

        var retryCheck = await CreateQueuedCheckAsync(database, monitor);
        var retryTransport = new RecordingSafeHttpTransport((request, call) =>
            call == 1 ? throw new HttpRequestException() : Success(request, call));
        var retryExecution = CreateExecutionService(database, retryTransport, true);
        (await retryExecution.ExecuteAsync(new(
            retryCheck.Id, retryCheck.DurableWork.Single().Id,
            "job-retry-1", "worker-a")))
            .Should().Be(LogicalCheckExecutionStatus.RetryRequired);
        (await database.CheckResults.AnyAsync(result => result.LogicalCheckId == retryCheck.Id))
            .Should().BeFalse();
        (await database.DurableWork.AsNoTracking()
            .SingleAsync(work => work.Id == retryCheck.DurableWork.Single().Id))
            .State.Should().Be(DurableWorkStates.Enqueued);
        (await retryExecution.ExecuteAsync(new(
            retryCheck.Id, retryCheck.DurableWork.Single().Id,
            "job-retry-2", "worker-a")))
            .Should().Be(LogicalCheckExecutionStatus.Completed);
        retryTransport.CallCount.Should().Be(2);
        (await database.CheckResults.CountAsync(result => result.LogicalCheckId == retryCheck.Id))
            .Should().Be(1);
        var retryAttempts = await database.ExecutionAttempts
            .Where(attempt => attempt.LogicalCheckId == retryCheck.Id)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToArrayAsync();
        retryAttempts.Select(attempt => attempt.AttemptNumber).Should().Equal(1, 2);
        retryAttempts.Select(attempt => attempt.InfrastructureOutcome).Should()
            .Equal(ExecutionAttemptOutcomes.RetryableFailure, ExecutionAttemptOutcomes.Succeeded);

        var timeoutCheck = await CreateQueuedCheckAsync(database, monitor);
        var timeoutTransport = new RecordingSafeHttpTransport((request, _) => Failure(
            request, SafeHttpFailureKind.Timeout));
        var timeoutExecution = CreateExecutionService(database, timeoutTransport, true);
        (await timeoutExecution.ExecuteAsync(new(
            timeoutCheck.Id, timeoutCheck.DurableWork.Single().Id,
            "job-timeout", "worker-a")))
            .Should().Be(LogicalCheckExecutionStatus.Completed);
        (await database.CheckResults.SingleAsync(result => result.LogicalCheckId == timeoutCheck.Id))
            .FailureCategory.Should().Be(HttpFailureCategories.Timeout);

        var ineligibleCheck = await CreateQueuedCheckAsync(database, monitor);
        var ineligibleTransport = new RecordingSafeHttpTransport(Success);
        var ineligibleExecution = CreateExecutionService(database, ineligibleTransport, false);
        (await ineligibleExecution.ExecuteAsync(new(
            ineligibleCheck.Id, ineligibleCheck.DurableWork.Single().Id,
            "job-ineligible", "worker-a")))
            .Should().Be(LogicalCheckExecutionStatus.Completed);
        ineligibleTransport.CallCount.Should().Be(0);
        var ineligibleResult = await database.CheckResults.SingleAsync(result =>
            result.LogicalCheckId == ineligibleCheck.Id);
        ineligibleResult.Outcome.Should().Be(HttpResultOutcomes.Cancelled);
        ineligibleResult.FailureCategory.Should().Be(HttpFailureCategories.TargetIneligible);
        ineligibleResult.CountsForUptime.Should().BeFalse();

        var cancelledCheck = await CreateQueuedCheckAsync(database, monitor);
        using var workerCancellation = new CancellationTokenSource();
        var cancelledTransport = new RecordingSafeHttpTransport((request, _) =>
        {
            workerCancellation.Cancel();
            return Failure(request, SafeHttpFailureKind.Cancelled);
        });
        var cancelledExecution = CreateExecutionService(database, cancelledTransport, true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledExecution.ExecuteAsync(new(
            cancelledCheck.Id, cancelledCheck.DurableWork.Single().Id,
            "job-cancelled", "worker-a"), workerCancellation.Token));
        (await database.CheckResults.AnyAsync(result => result.LogicalCheckId == cancelledCheck.Id))
            .Should().BeFalse();
        (await database.ExecutionAttempts.SingleAsync(attempt =>
            attempt.LogicalCheckId == cancelledCheck.Id)).InfrastructureOutcome.Should()
            .Be(ExecutionAttemptOutcomes.RetryableFailure);
        (await database.DurableWork.SingleAsync(work =>
            work.Id == cancelledCheck.DurableWork.Single().Id)).State.Should()
            .Be(DurableWorkStates.Enqueued);

        var leasedCheck = await CreateQueuedCheckAsync(database, monitor);
        var competingLeaseService = new ExecutionLeaseService(database);
        var competingClaim = await competingLeaseService.TryAcquireAsync(new(
            monitor.Id,
            leasedCheck.Id,
            Guid.NewGuid(),
            TimeSpan.FromMinutes(1)));
        competingClaim.Should().NotBeNull();
        var blockedTransport = new RecordingSafeHttpTransport(Success);
        var blockedExecution = CreateExecutionService(database, blockedTransport, true);
        (await blockedExecution.ExecuteAsync(new(
            leasedCheck.Id, leasedCheck.DurableWork.Single().Id,
            "job-competing", "worker-b")))
            .Should().Be(LogicalCheckExecutionStatus.RetryRequired);
        blockedTransport.CallCount.Should().Be(0);
        (await database.ExecutionAttempts.AnyAsync(attempt =>
            attempt.LogicalCheckId == leasedCheck.Id)).Should().BeFalse();
        await competingLeaseService.ReleaseAsync(competingClaim!);

        await VerifyFencedRetryAsync(database, monitor);
        await VerifyExecutionExhaustionAsync(database, monitor);
        await VerifyRetryBudgetExhaustionAsync(database, monitor);
    }

    private static async Task VerifyRetryBudgetExhaustionAsync(
        ApplicationDbContext database,
        EndpointMonitor monitor)
    {
        var exhaustedCheck = await CreateQueuedCheckAsync(database, monitor);
        var exhaustedWork = exhaustedCheck.DurableWork.Single();
        var exhaustedTransport = new RecordingSafeHttpTransport(
            (_, _) => throw new HttpRequestException("Simulated persistent transport fault."));
        var exhaustedExecution = CreateExecutionService(database, exhaustedTransport, true);

        (await exhaustedExecution.ExecuteAsync(new(
            exhaustedCheck.Id, exhaustedWork.Id, "job-exhaust-1", "worker-a")))
            .Should().Be(LogicalCheckExecutionStatus.RetryRequired);
        (await exhaustedExecution.ExecuteAsync(new(
            exhaustedCheck.Id, exhaustedWork.Id, "job-exhaust-2", "worker-a")))
            .Should().Be(LogicalCheckExecutionStatus.RetryRequired);
        (await exhaustedExecution.ExecuteAsync(new(
            exhaustedCheck.Id, exhaustedWork.Id, "job-exhaust-3", "worker-a")))
            .Should().Be(LogicalCheckExecutionStatus.Completed);
        exhaustedTransport.CallCount.Should().Be(3);

        var exhaustedResult = await database.CheckResults.AsNoTracking()
            .SingleAsync(result => result.LogicalCheckId == exhaustedCheck.Id);
        exhaustedResult.FailureCategory.Should().Be(HttpFailureCategories.ExecutionExhausted);
        exhaustedResult.Outcome.Should().Be(HttpResultOutcomes.Critical);
        (await database.DurableWork.AsNoTracking().SingleAsync(work => work.Id == exhaustedWork.Id))
            .State.Should().Be(DurableWorkStates.Completed);
        (await database.LogicalChecks.AsNoTracking().SingleAsync(check => check.Id == exhaustedCheck.Id))
            .State.Should().Be(LogicalCheckStates.Completed);

        // A stray duplicate job (e.g. from reconciliation racing the terminal finalize) must be a safe no-op.
        (await exhaustedExecution.ExecuteAsync(new(
            exhaustedCheck.Id, exhaustedWork.Id, "job-exhaust-4", "worker-a")))
            .Should().Be(LogicalCheckExecutionStatus.AlreadyCompleted);
        exhaustedTransport.CallCount.Should().Be(3);
        (await database.ExecutionAttempts.CountAsync(attempt => attempt.LogicalCheckId == exhaustedCheck.Id))
            .Should().Be(3);
    }

    private static async Task VerifyHealthConfirmationAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var database = new ApplicationDbContext(options.Options);
        var monitor = await AvailabilityMonitors(database)
            .Include(candidate => candidate.Endpoint)
                .ThenInclude(endpoint => endpoint.Environment)
                    .ThenInclude(environment => environment.Website)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstAsync();

        database.IssueStates.RemoveRange(database.IssueStates.Where(state => state.EndpointMonitorId == monitor.Id));
        database.EndpointHealth.RemoveRange(database.EndpointHealth.Where(health => health.EndpointMonitorId == monitor.Id));
        await database.SaveChangesAsync();
        await database.ExecutionLeases.Where(lease => lease.EndpointMonitorId == monitor.Id).ExecuteDeleteAsync();

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var previousIncident = new Incident
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = monitor.Id,
            OwnerSubjectId = monitor.Endpoint.OwnerSubjectId
                ?? monitor.Endpoint.Environment.Website.OwnerSubjectId,
            IssueKey = HttpIssueIdentity.Create("Http.ServerError"),
            Severity = IncidentSeverities.Critical,
            Status = IncidentStatuses.Closed,
            OpenedAt = clock.GetUtcNow().AddDays(-31),
            ResolvedAt = clock.GetUtcNow().AddDays(-30),
            ClosedAt = clock.GetUtcNow().AddDays(-30),
            ResolutionCategory = IncidentResolutionCategories.AutomaticRecovery,
            ResolutionNote = "Previous controlled incident.",
            OutageDurationMs = (long)TimeSpan.FromDays(1).TotalMilliseconds,
            Version = 1
        };
        database.Incidents.Add(previousIncident);
        await database.SaveChangesAsync();

        await FinalizeScheduledResultAsync(database, monitor, 500, clock);
        (await database.IssueStates.SingleAsync(state => state.EndpointMonitorId == monitor.Id))
            .ConsecutiveFailures.Should().Be(1);
        (await database.EndpointHealth.AnyAsync(health => health.EndpointMonitorId == monitor.Id))
            .Should().BeFalse();
        (await database.Incidents.AnyAsync(incident => incident.EndpointMonitorId == monitor.Id
            && incident.IssueKey == HttpIssueIdentity.Create("Http.ServerError")
            && IncidentStatuses.Active.Contains(incident.Status))).Should().BeFalse();

        var resetPass = await FinalizeScheduledResultAsync(database, monitor, 200, clock);
        var healthy = await database.EndpointHealth.SingleAsync(health => health.EndpointMonitorId == monitor.Id);
        healthy.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Healthy);
        healthy.EvidenceLogicalCheckId.Should().Be(resetPass);
        (await database.IssueStates.SingleAsync(state => state.EndpointMonitorId == monitor.Id))
            .ConsecutiveFailures.Should().Be(0);

        await FinalizeScheduledResultAsync(database, monitor, 500, clock);
        var confirmedFailure = await FinalizeScheduledResultAsync(database, monitor, 500, clock);
        healthy = await database.EndpointHealth.SingleAsync(health => health.EndpointMonitorId == monitor.Id);
        healthy.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Critical);
        healthy.EvidenceLogicalCheckId.Should().Be(confirmedFailure);
        var incident = await database.Incidents.Include(candidate => candidate.Evidence)
            .Include(candidate => candidate.Events)
            .SingleAsync(candidate => candidate.EndpointMonitorId == monitor.Id
                && candidate.IssueKey == HttpIssueIdentity.Create("Http.ServerError")
                && candidate.Status != IncidentStatuses.Closed);
        incident.Status.Should().Be(IncidentStatuses.Open);
        incident.PreviousIncidentId.Should().Be(previousIncident.Id);
        incident.RecurrenceCount.Should().Be(1);
        incident.OwnerSubjectId.Should().Be(
            monitor.Endpoint.OwnerSubjectId ?? monitor.Endpoint.Environment.Website.OwnerSubjectId);
        incident.Evidence.Should().ContainSingle(evidence =>
            evidence.EvidenceType == IncidentEvidenceTypes.Opening
            && evidence.LogicalCheckId == confirmedFailure);
        incident.Events.Should().Contain(eventRecord => eventRecord.EventType == IncidentEventTypes.Opened);

        // AC-03 / item 7: exactly one opening notification_event, with exactly one delivery, was
        // written by the automatic pipeline — no duplicates from the two failure-confirmation steps.
        var openedNotification = await database.NotificationEvents.AsNoTracking()
            .Include(notificationEvent => notificationEvent.Deliveries)
            .SingleAsync(notificationEvent => notificationEvent.IncidentId == incident.Id
                && notificationEvent.EventType == NotificationEventTypes.Opened);
        openedNotification.SourceKind.Should().Be(NotificationSourceKinds.IncidentEvent);
        openedNotification.IsSuppressed.Should().BeFalse();
        openedNotification.Deliveries.Should().ContainSingle();

        clock.Advance(TimeSpan.FromMinutes(1));
        await FinalizeScheduledResultAsync(database, monitor, 200, clock);
        healthy = await database.EndpointHealth.SingleAsync(health => health.EndpointMonitorId == monitor.Id);
        healthy.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Critical);
        healthy.EvidenceLogicalCheckId.Should().Be(confirmedFailure);
        (await database.IssueStates.SingleAsync(state => state.EndpointMonitorId == monitor.Id))
            .ConsecutiveRecoveries.Should().Be(1);
        incident = await database.Incidents.Include(candidate => candidate.Evidence)
            .SingleAsync(candidate => candidate.Id == incident.Id);
        incident.Status.Should().Be(IncidentStatuses.MonitoringRecovery);
        incident.RecoveryStartedAt.Should().NotBeNull();
        incident.Evidence.Should().Contain(evidence =>
            evidence.EvidenceType == IncidentEvidenceTypes.Recovery
            && evidence.EvidenceRole == "RecoveryStarted");

        // Item 5's "no recovery notification after only one passing check": the first pass must
        // not have created a Recovered notification_event yet.
        (await database.NotificationEvents.AnyAsync(notificationEvent =>
            notificationEvent.IncidentId == incident.Id
            && notificationEvent.EventType == NotificationEventTypes.Recovered)).Should().BeFalse();

        clock.Advance(TimeSpan.FromMinutes(1));
        var confirmedRecovery = await FinalizeScheduledResultAsync(database, monitor, 200, clock);
        healthy = await database.EndpointHealth.SingleAsync(health => health.EndpointMonitorId == monitor.Id);
        healthy.ConfirmedStatus.Should().Be(EndpointHealthStatuses.Healthy);
        healthy.EvidenceLogicalCheckId.Should().Be(confirmedRecovery);
        (await database.IssueStates.SingleAsync(state => state.EndpointMonitorId == monitor.Id))
            .ConsecutiveRecoveries.Should().Be(2);
        incident = await database.Incidents.Include(candidate => candidate.Evidence)
            .SingleAsync(candidate => candidate.Id == incident.Id);
        incident.Status.Should().Be(IncidentStatuses.Resolved);
        incident.RecoveryDurationMs.Should().BeGreaterThanOrEqualTo(0);
        incident.OutageDurationMs.Should().BeGreaterThanOrEqualTo(0);
        incident.Evidence.Should().Contain(evidence =>
            evidence.EvidenceType == IncidentEvidenceTypes.Resolution
            && evidence.LogicalCheckId == confirmedRecovery);
        (await database.AuditEvents.AnyAsync(audit => audit.EntityIdentifier == incident.Id.ToString()
            && audit.Action == "incident.resolved")).Should().BeTrue();

        // AC-04 / item 7: exactly one recovery notification_event, with exactly one delivery —
        // confirmed only on the second consecutive pass, never duplicated by the earlier pass.
        var recoveredNotification = await database.NotificationEvents.AsNoTracking()
            .Include(notificationEvent => notificationEvent.Deliveries)
            .SingleAsync(notificationEvent => notificationEvent.IncidentId == incident.Id
                && notificationEvent.EventType == NotificationEventTypes.Recovered);
        recoveredNotification.Deliveries.Should().ContainSingle();

        // Item 19: every automatic state-changing action (Opened, RecoveryStarted, Resolved — one
        // per call into IncidentAutomationService, each of which writes exactly one audit_event
        // alongside its timeline event(s)) has a matching audit_event with actor and timestamp.
        // Evidence-trail entries (EvidenceRecorded) are supplementary detail on the same audit
        // write, not separate auditable actions, so the two counts are not expected to match 1:1.
        var automaticTimelineEventTypes = await database.IncidentEvents.AsNoTracking()
            .Where(eventRecord => eventRecord.IncidentId == incident.Id)
            .Select(eventRecord => eventRecord.EventType)
            .ToArrayAsync();
        automaticTimelineEventTypes.Should().Contain(IncidentEventTypes.Opened);
        automaticTimelineEventTypes.Count(eventType => eventType == IncidentEventTypes.StatusChanged)
            .Should().Be(2);
        var automaticAuditActions = await database.AuditEvents.AsNoTracking()
            .Where(audit => audit.EntityIdentifier == incident.Id.ToString() && audit.Action.StartsWith("incident."))
            .Select(audit => new { audit.Action, audit.ActorUserId, audit.OccurredAt })
            .ToArrayAsync();
        automaticAuditActions.Select(audit => audit.Action).Should().BeEquivalentTo(
            ["incident.opened", "incident.recoverystarted", "incident.resolved"]);
        automaticAuditActions.Should().OnlyContain(audit => audit.OccurredAt != default);

        // This test never dispatches its notification deliveries — mark them Sent directly so
        // they cannot be batch-claimed by a later test's own NotificationDispatchService.DispatchDueAsync()
        // call (which claims any due delivery system-wide, with no per-test/incident scoping).
        await database.NotificationDeliveries
            .Where(delivery => delivery.NotificationEvent.IncidentId == incident.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(delivery => delivery.State, NotificationDeliveryStates.Sent)
                .SetProperty(delivery => delivery.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(delivery => delivery.SentAt, clock.GetUtcNow()));
    }

    /// <summary>
    /// Items 3 and 8: duplicate delivery of the same background job is modeled as two concurrent
    /// FinalizeAsync calls sharing the same lease claim/attempt/work IDs — exactly what a stray
    /// Hangfire retry or duplicate dispatch produces. The confirming (second) failure is finalized
    /// twice at once; the check-level lock already proves one Finalized/one AlreadyFinalized, and
    /// this extends that same race to prove it also yields exactly one incident, one Opened
    /// timeline event and one Opened notification — not two.
    /// </summary>
    private static async Task VerifyCompetingFinalizationOpensExactlyOneIncidentAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var database = new ApplicationDbContext(options.Options);
        var ownedMonitorId = await CreateOwnedMonitorIdAsync(
            connectionString, "http://competing-finalization.test/status");
        var monitor = await AvailabilityMonitors(database)
            .Include(candidate => candidate.Endpoint).ThenInclude(endpoint => endpoint.Environment)
            .SingleAsync(candidate => candidate.Id == ownedMonitorId);

        database.IssueStates.RemoveRange(database.IssueStates.Where(state => state.EndpointMonitorId == monitor.Id));
        database.EndpointHealth.RemoveRange(database.EndpointHealth.Where(health => health.EndpointMonitorId == monitor.Id));
        await database.SaveChangesAsync();
        await database.ExecutionLeases.Where(lease => lease.EndpointMonitorId == monitor.Id).ExecuteDeleteAsync();

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var raceIssueKey = HttpIssueIdentity.Create("Http.ServerError");
        await FinalizeScheduledResultAsync(database, monitor, 500, clock);
        (await database.IssueStates.SingleAsync(state =>
                state.EndpointMonitorId == monitor.Id && state.IssueKey == raceIssueKey))
            .ConsecutiveFailures.Should().Be(1);

        var check = await CreateQueuedCheckAsync(database, monitor);
        var work = check.DurableWork.Single();
        var now = clock.GetUtcNow();
        check.State = LogicalCheckStates.Running;
        check.StartedAt = now;
        work.State = DurableWorkStates.Processing;
        work.AttemptCount = 1;
        work.UpdatedAt = now;
        var attempt = new ExecutionAttempt
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = check.Id,
            AttemptNumber = 1,
            JobId = "race-confirming-failure",
            WorkerId = "race-verification",
            StartedAt = now,
            InfrastructureOutcome = ExecutionAttemptOutcomes.Running
        };
        database.ExecutionAttempts.Add(attempt);
        await database.SaveChangesAsync();

        var claim = await new ExecutionLeaseService(database).TryAcquireAsync(new(
            monitor.Id, check.Id, Guid.NewGuid(), TimeSpan.FromMinutes(1)));
        claim.Should().NotBeNull();
        var request = new SafeHttpTransportRequest(
            monitor.EndpointId,
            monitor.Endpoint.NormalizedUrl,
            monitor.Endpoint.Environment.IsProduction,
            check.ConfigurationSnapshot.MaxRedirects,
            check.ConfigurationSnapshot.MaxResponseBodyBytes,
            check.ConfigurationSnapshot.TimeoutSeconds);
        var result = new SafeHttpTransportResult(
            null, 500, new(new Uri(request.Url).GetLeftPart(UriPartial.Path)),
            TimeSpan.FromMilliseconds(25), 5, false, "error"u8.ToArray(), [],
            SafeHttpRequestIdentity.Create(request));
        var evidence = new HttpTransportEvidence(request, result);

        var competingOptions = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(competingOptions, connectionString);
        await using var competingDatabase = new ApplicationDbContext(competingOptions.Options);
        var finalizationA = CreateFinalizationService(database, clock);
        var finalizationB = CreateFinalizationService(competingDatabase, clock);

        var outcomes = await Task.WhenAll(
            finalizationA.FinalizeAsync(new(claim!, attempt.Id, work.Id, evidence)),
            finalizationB.FinalizeAsync(new(claim!, attempt.Id, work.Id, evidence)));
        outcomes.Should().BeEquivalentTo(
            [LogicalCheckFinalizationStatus.Finalized, LogicalCheckFinalizationStatus.AlreadyFinalized]);

        database.ChangeTracker.Clear();
        var issueKey = HttpIssueIdentity.Create("Http.ServerError");
        (await database.Incidents.CountAsync(incident => incident.EndpointMonitorId == monitor.Id
            && incident.IssueKey == issueKey)).Should().Be(1);
        var openedIncident = await database.Incidents.AsNoTracking()
            .SingleAsync(incident => incident.EndpointMonitorId == monitor.Id && incident.IssueKey == issueKey);
        (await database.IncidentEvents.CountAsync(eventRecord =>
            eventRecord.IncidentId == openedIncident.Id && eventRecord.EventType == IncidentEventTypes.Opened))
            .Should().Be(1);
        (await database.NotificationEvents.CountAsync(notificationEvent =>
            notificationEvent.IncidentId == openedIncident.Id
            && notificationEvent.EventType == NotificationEventTypes.Opened))
            .Should().Be(1);

        // Leaves nothing open/unacknowledged behind for the reminder/escalation sweep test to
        // accidentally pick up — that test asserts exact system-wide counts.
        await AcknowledgeAndResolveAsync(database, openedIncident.Id);
    }

    private static async Task CloseAllActiveIncidentsAsync(ApplicationDbContext database)
    {
        var activeIncidentIds = await database.Incidents.AsNoTracking()
            .Where(candidate => IncidentStatuses.Active.Contains(candidate.Status))
            .Select(candidate => candidate.Id)
            .ToArrayAsync();
        foreach (var incidentId in activeIncidentIds)
        {
            await AcknowledgeAndResolveAsync(database, incidentId);
        }
    }

    private static async Task AcknowledgeAndResolveAsync(
        ApplicationDbContext database, Guid incidentId, TimeProvider? timeProvider = null)
    {
        var lifecycle = new IncidentLifecycleService(
            database,
            new AlwaysAssignedAccessEvaluator(),
            new AuditTrailWriter(database),
            timeProvider ?? TimeProvider.System);
        var administratorId = await database.Users.AsNoTracking()
            .Where(user => user.Email == "bootstrap@example.test")
            .Select(user => user.Id)
            .SingleAsync();
        var access = new RegistryAccessContext(administratorId, [ApplicationRoles.Administrator]);
        var incident = await database.Incidents.AsNoTracking().SingleAsync(candidate => candidate.Id == incidentId);
        (await lifecycle.ResolveAsync(new(incidentId, incident.Version, "Cleanup", "Controlled test cleanup."), access))
            .Succeeded.Should().BeTrue();
        var resolved = await database.Incidents.AsNoTracking().SingleAsync(candidate => candidate.Id == incidentId);
        (await lifecycle.CloseAsync(new(incidentId, resolved.Version), access)).Succeeded.Should().BeTrue();
    }

    private sealed class AlwaysAssignedAccessEvaluator : IAssignmentAccessEvaluator
    {
        public Task<bool> IsAssignedAsync(
            Guid userId, Guid ownerSubjectId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    /// <summary>
    /// Item 4: two structurally different failure categories on the same monitor produce two
    /// separate incident rows, each keeping its own stable issue key, rather than one incident
    /// being reused or the second write colliding with the first. Confirmed sequentially (the
    /// first is resolved before the second opens) because the two categories otherwise reset each
    /// other's confirmation counters — see HealthConfirmationEngine.EvaluateFailure, which zeroes
    /// any tracked issue key not observed by the current check. The active-incident uniqueness
    /// index (VerifyDuplicateActiveIncidentRejectedAsync) already proves the constraint is scoped
    /// per issue key, not per monitor, so together these show distinct keys neither merge nor
    /// collide even though this test does not hold them open at the same instant.
    /// </summary>
    private static async Task VerifyDistinctIssueKeysCreateDistinctIncidentsAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var database = new ApplicationDbContext(options.Options);
        var ownedMonitorId = await CreateOwnedMonitorIdAsync(
            connectionString, "http://distinct-issue-keys.test/status");
        var monitor = await AvailabilityMonitors(database)
            .Include(candidate => candidate.Endpoint).ThenInclude(endpoint => endpoint.Environment)
            .SingleAsync(candidate => candidate.Id == ownedMonitorId);

        database.IssueStates.RemoveRange(database.IssueStates.Where(state => state.EndpointMonitorId == monitor.Id));
        database.EndpointHealth.RemoveRange(database.EndpointHealth.Where(health => health.EndpointMonitorId == monitor.Id));
        await database.SaveChangesAsync();
        await database.ExecutionLeases.Where(lease => lease.EndpointMonitorId == monitor.Id).ExecuteDeleteAsync();

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await FinalizeScheduledResultAsync(database, monitor, 500, clock);
        await FinalizeScheduledResultAsync(database, monitor, 500, clock);
        var serverErrorKey = HttpIssueIdentity.Create("Http.ServerError");
        var serverErrorIncident = await database.Incidents.AsNoTracking().SingleAsync(incident =>
            incident.EndpointMonitorId == monitor.Id && incident.IssueKey == serverErrorKey);
        serverErrorIncident.Status.Should().Be(IncidentStatuses.Open);

        clock.Advance(TimeSpan.FromMinutes(1));
        await FinalizeScheduledResultAsync(database, monitor, 200, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        await FinalizeScheduledResultAsync(database, monitor, 200, clock);
        (await database.Incidents.AsNoTracking().SingleAsync(incident => incident.Id == serverErrorIncident.Id))
            .Status.Should().Be(IncidentStatuses.Resolved);

        await FinalizeScheduledResultAsync(database, monitor, 404, clock);
        await FinalizeScheduledResultAsync(database, monitor, 404, clock);
        var clientErrorKey = HttpIssueIdentity.Create("Http.ClientError");
        var clientErrorIncident = await database.Incidents.AsNoTracking().SingleAsync(incident =>
            incident.EndpointMonitorId == monitor.Id && incident.IssueKey == clientErrorKey);
        clientErrorIncident.Status.Should().Be(IncidentStatuses.Open);
        clientErrorIncident.Id.Should().NotBe(serverErrorIncident.Id);
        clientErrorIncident.IssueKey.Should().NotBe(serverErrorIncident.IssueKey);

        // Leaves nothing open/unacknowledged behind for the reminder/escalation sweep test. Uses
        // the same clock this incident was opened with — it was advanced ahead of real time, and
        // TimeProvider.System would otherwise resolve it before its own OpenedAt.
        await AcknowledgeAndResolveAsync(database, clientErrorIncident.Id, clock);
    }

    private static async Task<Guid> FinalizeScheduledResultAsync(
        ApplicationDbContext database,
        EndpointMonitor monitor,
        int statusCode,
        TimeProvider? timeProvider = null,
        string? htmlBody = null)
    {
        var check = await CreateQueuedCheckAsync(database, monitor, timeProvider);
        var work = check.DurableWork.Single();
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        check.State = LogicalCheckStates.Running;
        check.StartedAt = now;
        work.State = DurableWorkStates.Processing;
        work.AttemptCount = 1;
        work.UpdatedAt = now;
        var attempt = new ExecutionAttempt
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = check.Id,
            AttemptNumber = 1,
            JobId = $"health-{check.Id:N}",
            WorkerId = "health-verification",
            StartedAt = now,
            InfrastructureOutcome = ExecutionAttemptOutcomes.Running
        };
        database.ExecutionAttempts.Add(attempt);
        await database.SaveChangesAsync();

        var claim = await new ExecutionLeaseService(database).TryAcquireAsync(new(
            monitor.Id, check.Id, Guid.NewGuid(), TimeSpan.FromMinutes(1)));
        claim.Should().NotBeNull();
        var request = new SafeHttpTransportRequest(
            monitor.EndpointId,
            monitor.Endpoint.NormalizedUrl,
            monitor.Endpoint.Environment.IsProduction,
            check.ConfigurationSnapshot.MaxRedirects,
            check.ConfigurationSnapshot.MaxResponseBodyBytes,
            check.ConfigurationSnapshot.TimeoutSeconds);
        // Finalization rejects evidence that read fewer bytes off the wire than it kept, so the
        // read count has to follow the body rather than sit at the two bytes the default "ok"
        // response happens to be.
        var body = htmlBody is null ? "ok"u8.ToArray() : Encoding.UTF8.GetBytes(htmlBody);
        var result = new SafeHttpTransportResult(
            null,
            statusCode,
            new(new Uri(request.Url).GetLeftPart(UriPartial.Path)),
            TimeSpan.FromMilliseconds(25),
            body.Length,
            false,
            body,
            [],
            SafeHttpRequestIdentity.Create(request),
            ContentType: htmlBody is null ? null : "text/html; charset=utf-8");
        var finalization = CreateFinalizationService(database, timeProvider ?? TimeProvider.System);

        (await finalization.FinalizeAsync(new(
            claim!, attempt.Id, work.Id, new HttpTransportEvidence(request, result))))
            .Should().Be(LogicalCheckFinalizationStatus.Finalized);
        return check.Id;
    }

    private static async Task VerifyExecutionExhaustionAsync(
        ApplicationDbContext database,
        EndpointMonitor monitor)
    {
        var check = await CreateQueuedCheckAsync(database, monitor);
        var work = check.DurableWork.Single();
        var now = DateTimeOffset.UtcNow;
        check.State = LogicalCheckStates.Running;
        check.StartedAt = now;
        work.State = DurableWorkStates.Processing;
        work.AttemptCount = 1;
        work.UpdatedAt = now;
        var attempt = new ExecutionAttempt
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = check.Id,
            AttemptNumber = 1,
            JobId = "exhausted-job",
            WorkerId = "worker-a",
            StartedAt = now,
            InfrastructureOutcome = ExecutionAttemptOutcomes.Running
        };
        database.ExecutionAttempts.Add(attempt);
        await database.SaveChangesAsync();
        var claim = await new ExecutionLeaseService(database).TryAcquireAsync(new(
            monitor.Id, check.Id, Guid.NewGuid(), TimeSpan.FromMinutes(1)));
        claim.Should().NotBeNull();

        var finalization = CreateFinalizationService(database, TimeProvider.System);
        (await finalization.FinalizeAsync(new(
            claim!, attempt.Id, work.Id,
            new ExecutionTerminalEvidence(ExecutionTerminalReason.RetriesExhausted))))
            .Should().Be(LogicalCheckFinalizationStatus.Finalized);
        (await database.CheckResults.AsNoTracking().SingleAsync(result =>
            result.LogicalCheckId == check.Id)).FailureCategory.Should()
            .Be(HttpFailureCategories.ExecutionExhausted);
        (await database.ExecutionAttempts.AsNoTracking().SingleAsync(candidate =>
            candidate.Id == attempt.Id)).InfrastructureOutcome.Should()
            .Be(ExecutionAttemptOutcomes.TerminalFailure);
    }

    private static async Task VerifyFencedRetryAsync(
        ApplicationDbContext database,
        EndpointMonitor monitor)
    {
        var check = await CreateQueuedCheckAsync(database, monitor);
        var work = check.DurableWork.Single();
        var leaseService = new ExecutionLeaseService(database);
        var staleClaim = await leaseService.TryAcquireAsync(new(
            monitor.Id, check.Id, Guid.NewGuid(), TimeSpan.FromMinutes(1)));
        staleClaim.Should().NotBeNull();

        var now = DateTimeOffset.UtcNow;
        check.State = LogicalCheckStates.Running;
        check.StartedAt = now;
        work.State = DurableWorkStates.Processing;
        work.AttemptCount = 1;
        work.UpdatedAt = now;
        var staleAttempt = new ExecutionAttempt
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = check.Id,
            AttemptNumber = 1,
            JobId = "stale-job",
            WorkerId = "worker-a",
            StartedAt = now,
            InfrastructureOutcome = ExecutionAttemptOutcomes.Running
        };
        database.ExecutionAttempts.Add(staleAttempt);
        await database.SaveChangesAsync();
        await leaseService.ReleaseAsync(staleClaim!);

        var winningClaim = await leaseService.TryAcquireAsync(new(
            monitor.Id, check.Id, Guid.NewGuid(), TimeSpan.FromMinutes(1)));
        winningClaim.Should().NotBeNull();
        var winningAttempt = new ExecutionAttempt
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = check.Id,
            AttemptNumber = 2,
            JobId = "winning-job",
            WorkerId = "worker-b",
            StartedAt = now.AddMilliseconds(1),
            InfrastructureOutcome = ExecutionAttemptOutcomes.Running
        };
        database.ExecutionAttempts.Add(winningAttempt);
        await database.SaveChangesAsync();

        var finalization = CreateFinalizationService(database, TimeProvider.System);
        (await finalization.PrepareRetryAsync(new(
            staleClaim!, staleAttempt.Id, work.Id, "Infrastructure")))
            .Should().Be(LogicalCheckRetryStatus.Superseded);
        (await database.DurableWork.AsNoTracking().SingleAsync(candidate => candidate.Id == work.Id))
            .State.Should().Be(DurableWorkStates.Processing);

        var request = new SafeHttpTransportRequest(
            monitor.EndpointId,
            monitor.Endpoint.NormalizedUrl,
            monitor.Endpoint.Environment.IsProduction,
            check.ConfigurationSnapshot.MaxRedirects,
            check.ConfigurationSnapshot.MaxResponseBodyBytes,
            check.ConfigurationSnapshot.TimeoutSeconds);
        (await finalization.FinalizeAsync(new(
            winningClaim!, winningAttempt.Id, work.Id,
            new HttpTransportEvidence(request, Success(request, 1)))))
            .Should().Be(LogicalCheckFinalizationStatus.Finalized);
        (await database.ExecutionAttempts.AsNoTracking()
            .Where(attempt => attempt.LogicalCheckId == check.Id)
            .OrderBy(attempt => attempt.AttemptNumber)
            .Select(attempt => attempt.InfrastructureOutcome)
            .ToArrayAsync()).Should().Equal(
                ExecutionAttemptOutcomes.Superseded,
                ExecutionAttemptOutcomes.Succeeded);
        (await database.CheckResults.CountAsync(result => result.LogicalCheckId == check.Id))
            .Should().Be(1);
    }

    private static LogicalCheckExecutionService CreateExecutionService(
        ApplicationDbContext database,
        ISafeHttpTransport transport,
        bool isEligible)
    {
        var timeProvider = TimeProvider.System;
        var leaseService = new ExecutionLeaseService(database);
        var finalizationService = CreateFinalizationService(database, timeProvider);
        return new(
            database,
            new FixedEligibilityService(isEligible),
            leaseService,
            transport,
            new UnusedSslCertificateProbe(),
            finalizationService,
            timeProvider,
            NullLogger<LogicalCheckExecutionService>.Instance);
    }

    /// <summary>These availability-path assertions never reach the certificate probe.</summary>
    private sealed class UnusedSslCertificateProbe : ISslCertificateProbe
    {
        public Task<SslCertificateProbeResult> ProbeAsync(
            SslCertificateProbeRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoUrgentSslChecks : ISslUrgentCheckScheduler
    {
        public Task<UrgentCertificateCheck?> PrepareAfterTlsFailureAsync(
            Guid endpointId,
            LogicalCheckTerminalEvidence evidence,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UrgentCertificateCheck?>(null);

        public Task EnqueueAsync(
            UrgentCertificateCheck request,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static LogicalCheckFinalizationService CreateFinalizationService(
        ApplicationDbContext database,
        TimeProvider timeProvider) =>
        new(
            database,
            new MaintenanceEvaluator(database),
            new IncidentAutomationService(
                database, new AuditTrailWriter(database), new NotificationEventWriter(database)),
            new NoUrgentSslChecks(),
            new SeoValueExtractor(),
            new SafeHttpTransportOptions(),
            timeProvider);

    private static async Task<LogicalCheck> CreateQueuedCheckAsync(
        ApplicationDbContext database,
        EndpointMonitor monitor,
        TimeProvider? timeProvider = null)
    {
        var sequence = Interlocked.Increment(ref fixtureSequence);
        // The sequence only has to space fixtures apart, but it is added to the base, so the base
        // has to sit far enough back that a two-digit sequence cannot carry createdAt past the
        // caller's clock — durable_work's updated_at >= created_at check rejects that. It also has
        // to stay recent enough to fall inside the maintenance windows these fixtures open five
        // minutes back. Reading the caller's clock rather than the system one keeps the comparison
        // against a single time source when a frozen TimeProvider has drifted behind real time.
        // Milliseconds, so the spacing orders fixtures apart without the count being able to walk
        // createdAt up to the caller's clock: seconds gave this a ceiling of two minutes' worth of
        // checks, and crossing it dated a check into the future where durable_work's
        // updated_at >= created_at check rejected it.
        var createdAt = (timeProvider ?? TimeProvider.System).GetUtcNow()
            .AddMinutes(-2).AddMilliseconds(sequence);
        var check = new LogicalCheck
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = monitor.Id,
            Source = LogicalCheckSources.Scheduled,
            ScheduledFor = createdAt,
            State = LogicalCheckStates.Queued,
            CadenceKey = MonitorCadence.CreateCadenceKey(monitor.Id, createdAt),
            PolicyFingerprint = monitor.ConfigurationFingerprint,
            CreatedAt = createdAt,
            QueuedAt = createdAt
        };
        check.ConfigurationSnapshot = new CheckConfigurationSnapshot
        {
            LogicalCheckId = check.Id,
            SchemaVersion = 2,
            MonitorType = monitor.MonitorType,
            ConfigurationFingerprint = monitor.ConfigurationFingerprint,
            IntervalSeconds = monitor.IntervalSeconds,
            TimeoutSeconds = monitor.TimeoutSeconds,
            FailureConfirmationCount = monitor.FailureConfirmationCount,
            RecoveryConfirmationCount = monitor.RecoveryConfirmationCount,
            WarningThresholdMs = monitor.WarningThresholdMs,
            CriticalThresholdMs = monitor.CriticalThresholdMs,
            IntervalSource = ConfigurationValueSources.EnvironmentDefault,
            TimeoutSource = ConfigurationValueSources.PolicyProfile,
            ConfirmationSource = ConfigurationValueSources.PolicyProfile,
            ThresholdSource = ConfigurationValueSources.PolicyProfile,
            CreatedAt = createdAt
        };
        check.DurableWork.Add(new DurableWork
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = check.Id,
            WorkKind = DurableWorkKinds.HttpCheck,
            DedupeKey = $"v1|{check.Id:N}|http-check",
            QueueName = "monitoring",
            State = DurableWorkStates.Enqueued,
            AvailableAt = createdAt,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        });
        database.LogicalChecks.Add(check);
        await database.SaveChangesAsync();
        return check;
    }

    private static SafeHttpTransportResult Success(
        SafeHttpTransportRequest request,
        int _) => new(
            null,
            200,
            new(new Uri(request.Url).GetLeftPart(UriPartial.Path)),
            TimeSpan.FromMilliseconds(25),
            2,
            false,
            "ok"u8.ToArray(),
            [],
            SafeHttpRequestIdentity.Create(request));

    private static SafeHttpTransportResult Failure(
        SafeHttpTransportRequest request,
        SafeHttpFailureKind failure) => new(
            failure,
            null,
            null,
            TimeSpan.FromMilliseconds(25),
            0,
            false,
            ReadOnlyMemory<byte>.Empty,
            [],
            SafeHttpRequestIdentity.Create(request));

    private sealed class FixedEligibilityService(bool isEligible) : IMonitoringEligibilityService
    {
        public Task<bool> IsEndpointEligibleAsync(
            Guid endpointId,
            CancellationToken cancellationToken = default) => Task.FromResult(isEligible);

        public Task<bool> IsEndpointTestableAsync(
            Guid endpointId,
            CancellationToken cancellationToken = default) => Task.FromResult(isEligible);
    }

    private sealed class RecordingSafeHttpTransport(
        Func<SafeHttpTransportRequest, int, SafeHttpTransportResult> handler) : ISafeHttpTransport
    {
        public int CallCount { get; private set; }
        public SafeHttpTransportRequest? LastRequest { get; private set; }

        public Task<SafeHttpTransportResult> SendAsync(
            SafeHttpTransportRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(handler(request, CallCount));
        }
    }

    private static async Task VerifyDuplicateCadenceRejectedAsync(
        string connectionString,
        Guid monitorId,
        string fingerprint,
        DateTimeOffset scheduledFor,
        string cadenceKey)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO web_health.logical_check
                (id, endpoint_monitor_id, source, scheduled_for, state, cadence_key, policy_fingerprint, created_at)
            VALUES (@id, @monitor_id, 'Scheduled', @scheduled_for, 'Pending', @cadence_key, @fingerprint, @created_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("monitor_id", monitorId);
        command.Parameters.AddWithValue("scheduled_for", scheduledFor);
        command.Parameters.AddWithValue("cadence_key", cadenceKey);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.AddWithValue("created_at", scheduledFor);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        exception.ConstraintName.Should().Be("ix_logical_check_endpoint_monitor_id_cadence_key");
    }

    private static async Task VerifySnapshotIsImmutableAsync(string connectionString, Guid logicalCheckId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.check_configuration_snapshot SET schema_version = 2 WHERE logical_check_id = @id",
            connection);
        command.Parameters.AddWithValue("id", logicalCheckId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.RaiseException);
    }

    private static async Task VerifyMissingSnapshotRejectedAsync(
        string connectionString,
        Guid monitorId,
        string fingerprint,
        DateTimeOffset now)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string sql = """
            INSERT INTO web_health.logical_check
                (id, endpoint_monitor_id, source, scheduled_for, state, cadence_key,
                 policy_fingerprint, created_at, queued_at)
            VALUES (@id, @monitor_id, 'Scheduled', @now, 'Queued', @cadence_key,
                    @fingerprint, @now, @now);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("monitor_id", monitorId);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("cadence_key", $"missing-snapshot-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        await command.ExecuteNonQueryAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.ConstraintName.Should().Be("ck_logical_check_nonpending_snapshot");
    }

    private static async Task VerifySystemUrgentCheckAsync(
        string connectionString,
        Guid monitorId,
        string fingerprint,
        DateTimeOffset now)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var urgentCheckId = Guid.NewGuid();
        const string urgentSql = """
            INSERT INTO web_health.logical_check
                (id, endpoint_monitor_id, source, requested_at, state, policy_fingerprint, created_at)
            VALUES (@id, @monitor_id, 'Urgent', @now, 'Pending', @fingerprint, @now);
            """;
        await using (var urgent = new NpgsqlCommand(urgentSql, connection))
        {
            urgent.Parameters.AddWithValue("id", urgentCheckId);
            urgent.Parameters.AddWithValue("monitor_id", monitorId);
            urgent.Parameters.AddWithValue("now", now);
            urgent.Parameters.AddWithValue("fingerprint", fingerprint);
            (await urgent.ExecuteNonQueryAsync()).Should().Be(1);
        }

        const string manualSql = """
            INSERT INTO web_health.logical_check
                (id, endpoint_monitor_id, source, requested_at, state, policy_fingerprint, created_at)
            VALUES (@id, @monitor_id, 'Manual', @now, 'Pending', @fingerprint, @now);
            """;
        await using var manual = new NpgsqlCommand(manualSql, connection);
        manual.Parameters.AddWithValue("id", Guid.NewGuid());
        manual.Parameters.AddWithValue("monitor_id", monitorId);
        manual.Parameters.AddWithValue("now", now);
        manual.Parameters.AddWithValue("fingerprint", fingerprint);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => manual.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("ck_logical_check_source_fields");
    }

    private static async Task VerifyNegativeThresholdsRejectedAsync(
        string connectionString,
        Guid monitorId,
        string fingerprint,
        DateTimeOffset now)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var monitor = new NpgsqlCommand(
            "UPDATE web_health.endpoint_monitor SET warning_threshold_ms = -1 WHERE id = @id",
            connection))
        {
            monitor.Parameters.AddWithValue("id", monitorId);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => monitor.ExecuteNonQueryAsync());
            exception.ConstraintName.Should().Be("ck_endpoint_monitor_threshold_order");
        }

        await using var transaction = await connection.BeginTransactionAsync();
        var checkId = Guid.NewGuid();
        const string checkSql = """
            INSERT INTO web_health.logical_check
                (id, endpoint_monitor_id, source, requested_at, state, policy_fingerprint, created_at)
            VALUES (@id, @monitor_id, 'Urgent', @now, 'Pending', @fingerprint, @now);
            """;
        await using (var check = new NpgsqlCommand(checkSql, connection, transaction))
        {
            check.Parameters.AddWithValue("id", checkId);
            check.Parameters.AddWithValue("monitor_id", monitorId);
            check.Parameters.AddWithValue("now", now);
            check.Parameters.AddWithValue("fingerprint", fingerprint);
            await check.ExecuteNonQueryAsync();
        }

        const string snapshotSql = """
            INSERT INTO web_health.check_configuration_snapshot
                (logical_check_id, schema_version, monitor_type, configuration_fingerprint,
                 interval_seconds, timeout_seconds, failure_confirmation_count, recovery_confirmation_count,
                 warning_threshold_ms, critical_threshold_ms, interval_source, timeout_source,
                 confirmation_source, threshold_source, created_at)
            VALUES (@id, 1, 'HttpAvailability', @fingerprint,
                    300, 15, 2, 2, -1, 3000, 'EnvironmentDefault', 'PolicyProfile',
                    'PolicyProfile', 'PolicyProfile', @now);
            """;
        await using var snapshot = new NpgsqlCommand(snapshotSql, connection, transaction);
        snapshot.Parameters.AddWithValue("id", checkId);
        snapshot.Parameters.AddWithValue("fingerprint", fingerprint);
        snapshot.Parameters.AddWithValue("now", now);
        var snapshotException = await Assert.ThrowsAsync<PostgresException>(() => snapshot.ExecuteNonQueryAsync());
        snapshotException.ConstraintName.Should().Be("ck_check_configuration_snapshot_threshold_order");
        await transaction.RollbackAsync();
    }

    private static async Task VerifyHangfireSchedulingAsync(string connectionString)
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddDays(1));
        var queue = new RecordingLogicalCheckQueue();
        await using var services = BuildSchedulingServices(connectionString, clock, queue);
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var scheduling = scope.ServiceProvider.GetRequiredService<IMonitoringSchedulingService>();

        await database.DurableWork
            .Where(work => work.State != DurableWorkStates.Completed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                work => work.State, DurableWorkStates.Completed));

        var hangfireTables = await database.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'hangfire'
            """).SingleAsync();
        hangfireTables.Should().BeGreaterThan(0);

        var eligibleEndpointIds = MonitoringEligibility.Apply(
                database.Endpoints.AsNoTracking(), clock.GetUtcNow())
            .Select(endpoint => endpoint.Id);
        var monitor = await AvailabilityMonitors(database)
            .Include(candidate => candidate.Endpoint)
                .ThenInclude(endpoint => endpoint.Environment)
            // Production, because the snapshot below takes its interval from the environment
            // default and the assertion on it is the production one. Ordered by creation so the
            // stage keeps its monitor as fixtures are added around it.
            .Where(candidate => candidate.DeletedAt == null
                && candidate.Endpoint.Environment.IsProduction
                && eligibleEndpointIds.Contains(candidate.EndpointId))
            .OrderBy(candidate => candidate.CreatedAt).ThenBy(candidate => candidate.Id)
            .FirstAsync();
        var otherEnabledMonitorIds = await AvailabilityMonitors(database).AsNoTracking()
            .Where(candidate => candidate.Id != monitor.Id && candidate.IsEnabled)
            .Select(candidate => candidate.Id)
            .ToArrayAsync();
        await AvailabilityMonitors(database)
            .Where(candidate => otherEnabledMonitorIds.Contains(candidate.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.IsEnabled, false)
                .SetProperty(candidate => candidate.NextDueAt, clock.GetUtcNow().AddDays(1)));
        monitor.ScheduleAnchor = clock.GetUtcNow().AddHours(-1);
        monitor.NextDueAt = clock.GetUtcNow().AddMinutes(-30);
        monitor.IntervalSeconds = MonitorCadence.ProductionDefaultIntervalSeconds;
        monitor.BoundedOverrides = "{}";
        monitor.ConfigurationFingerprint = RegistryDefaults.CreateHttpFingerprint(
            monitor.Endpoint.NormalizedUrl,
            monitor.Endpoint.Environment.IsProduction,
            monitor.IntervalSeconds,
            monitor.TimeoutSeconds,
            monitor.FailureConfirmationCount,
            monitor.RecoveryConfirmationCount,
            monitor.WarningThresholdMs,
            monitor.CriticalThresholdMs);
        await database.SaveChangesAsync();

        var firstDispatch = await scheduling.DispatchDueAsync();
        firstDispatch.Should().Be(new MonitoringDispatchResult(1, 1));
        database.ChangeTracker.Clear();
        monitor = await AvailabilityMonitors(database)
            .Include(candidate => candidate.Endpoint)
            .SingleAsync(candidate => candidate.Id == monitor.Id);
        var firstCheck = await database.LogicalChecks
            .Include(check => check.ConfigurationSnapshot)
            .Include(check => check.DurableWork)
            .SingleAsync(check => check.Id == queue.Jobs.Single().LogicalCheckId);
        firstCheck.ScheduledFor.Should().BeOnOrBefore(clock.GetUtcNow());
        firstCheck.State.Should().Be(LogicalCheckStates.Queued);
        firstCheck.ConfigurationSnapshot.IntervalSeconds.Should().Be(300);
        firstCheck.ConfigurationSnapshot.IntervalSource.Should().Be(ConfigurationValueSources.EnvironmentDefault);
        firstCheck.DurableWork.Single().State.Should().Be(DurableWorkStates.Enqueued);
        monitor.NextDueAt.Should().BeAfter(clock.GetUtcNow());
        (await scheduling.DispatchDueAsync()).Should().Be(new MonitoringDispatchResult(0, 0));

        firstCheck.DurableWork.Single().State = DurableWorkStates.Completed;
        monitor.Endpoint.IsEnabled = false;
        var disabledEndpointDueAt = clock.GetUtcNow();
        monitor.NextDueAt = disabledEndpointDueAt;
        await database.SaveChangesAsync();
        var checksBeforeDisabledDispatch = await database.LogicalChecks.CountAsync(
            check => check.EndpointMonitorId == monitor.Id);
        (await scheduling.DispatchDueAsync()).Should().Be(new MonitoringDispatchResult(0, 0));
        // Claimed-but-ineligible monitors still advance cadence so they aren't re-claimed every tick.
        (await AvailabilityMonitors(database).AsNoTracking()
            .Where(candidate => candidate.Id == monitor.Id)
            .Select(candidate => candidate.NextDueAt)
            .SingleAsync()).Should().BeAfter(disabledEndpointDueAt);
        (await database.LogicalChecks.CountAsync(check => check.EndpointMonitorId == monitor.Id))
            .Should().Be(checksBeforeDisabledDispatch);

        monitor.Endpoint.IsEnabled = true;
        await database.SaveChangesAsync();
        await VerifySuppressedSchedulingAsync(
            database, scheduling, clock, monitor.Id,
            candidate => candidate.Endpoint.Environment.Website.Client.IsActive = false,
            candidate => candidate.Endpoint.Environment.Website.Client.IsActive = true);
        await VerifySuppressedSchedulingAsync(
            database, scheduling, clock, monitor.Id,
            candidate => candidate.Endpoint.Environment.Website.IsEnabled = false,
            candidate => candidate.Endpoint.Environment.Website.IsEnabled = true);
        await VerifySuppressedSchedulingAsync(
            database, scheduling, clock, monitor.Id,
            candidate => candidate.Endpoint.Environment.IsActive = false,
            candidate => candidate.Endpoint.Environment.IsActive = true);
        await VerifySuppressedSchedulingAsync(
            database, scheduling, clock, monitor.Id,
            candidate => candidate.IsEnabled = false,
            candidate => candidate.IsEnabled = true,
            advancesCadence: false);
        await VerifySuppressedSchedulingAsync(
            database, scheduling, clock, monitor.Id,
            candidate => candidate.Endpoint.TargetAuthorizations.Single(
                evidence => evidence.RevokedAt == null).ExpiresAt = clock.GetUtcNow(),
            candidate => candidate.Endpoint.TargetAuthorizations.Single(
                evidence => evidence.RevokedAt == null).ExpiresAt = null);

        database.ChangeTracker.Clear();
        monitor = await database.EndpointMonitors.SingleAsync(candidate => candidate.Id == monitor.Id);
        clock.Advance(TimeSpan.FromMinutes(10));
        monitor.NextDueAt = clock.GetUtcNow();
        queue.FailNext = true;
        await database.SaveChangesAsync();
        var interrupted = await scheduling.DispatchDueAsync();
        interrupted.Should().Be(new MonitoringDispatchResult(1, 0));
        database.ChangeTracker.Clear();
        var interruptedWork = await database.DurableWork
            .OrderByDescending(work => work.CreatedAt)
            .FirstAsync(work => work.LogicalCheck.EndpointMonitorId == monitor.Id);
        interruptedWork.State.Should().Be(DurableWorkStates.Dispatching);
        var checkCount = await database.LogicalChecks.CountAsync(
            check => check.EndpointMonitorId == monitor.Id);

        clock.Advance(TimeSpan.FromMinutes(3));
        var recovered = await scheduling.ReconcileAsync();
        recovered.Should().Be(new MonitoringDispatchResult(1, 1));
        (await database.DurableWork.AsNoTracking()
            .Where(work => work.Id == interruptedWork.Id)
            .Select(work => work.State)
            .SingleAsync()).Should().Be(DurableWorkStates.Enqueued);
        (await database.LogicalChecks.CountAsync(
            check => check.EndpointMonitorId == monitor.Id)).Should().Be(checkCount);

        await database.DurableWork.ExecuteUpdateAsync(setters => setters
            .SetProperty(work => work.State, DurableWorkStates.Completed));
        clock.Advance(TimeSpan.FromMinutes(10));
        monitor.NextDueAt = clock.GetUtcNow();
        await database.SaveChangesAsync();
        await using var competingServices = BuildSchedulingServices(connectionString, clock, queue);
        await using var firstScope = competingServices.CreateAsyncScope();
        await using var secondScope = competingServices.CreateAsyncScope();
        var competingResults = await Task.WhenAll(
            firstScope.ServiceProvider.GetRequiredService<IMonitoringSchedulingService>().DispatchDueAsync(),
            secondScope.ServiceProvider.GetRequiredService<IMonitoringSchedulingService>().DispatchDueAsync());
        competingResults.Sum(result => result.ClaimedCount).Should().Be(1);
        competingResults.Sum(result => result.EnqueuedCount).Should().Be(1);
        await database.EndpointMonitors
            .Where(candidate => otherEnabledMonitorIds.Contains(candidate.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.IsEnabled, true));

        var environmentId = await database.Endpoints.AsNoTracking()
            .Where(candidate => candidate.Id == monitor.EndpointId)
            .Select(candidate => candidate.EnvironmentId)
            .SingleAsync();
        await VerifyClaimQueryLocksHierarchyAsync(connectionString, monitor.Id, environmentId);
    }

    private static async Task VerifySuppressedSchedulingAsync(
        ApplicationDbContext database,
        IMonitoringSchedulingService scheduling,
        MutableTimeProvider clock,
        Guid monitorId,
        Action<EndpointMonitor> suppress,
        Action<EndpointMonitor> restore,
        bool advancesCadence = true)
    {
        database.ChangeTracker.Clear();
        var monitor = await database.EndpointMonitors
            .Include(candidate => candidate.Endpoint)
                .ThenInclude(endpoint => endpoint.Environment)
                    .ThenInclude(environment => environment.Website)
                        .ThenInclude(website => website.Client)
            .Include(candidate => candidate.Endpoint)
                .ThenInclude(endpoint => endpoint.TargetAuthorizations)
            .SingleAsync(candidate => candidate.Id == monitorId);
        var dueAt = clock.GetUtcNow();
        monitor.NextDueAt = dueAt;
        suppress(monitor);
        await database.SaveChangesAsync();

        var checksBeforeDispatch = await database.LogicalChecks.CountAsync(
            check => check.EndpointMonitorId == monitor.Id);
        (await scheduling.DispatchDueAsync()).Should().Be(new MonitoringDispatchResult(0, 0));
        (await database.LogicalChecks.CountAsync(check => check.EndpointMonitorId == monitor.Id))
            .Should().Be(checksBeforeDispatch);
        if (advancesCadence)
        {
            (await database.EndpointMonitors.AsNoTracking()
                .Where(candidate => candidate.Id == monitor.Id)
                .Select(candidate => candidate.NextDueAt)
                .SingleAsync()).Should().BeAfter(dueAt);
        }

        restore(monitor);
        monitor.NextDueAt = clock.GetUtcNow().AddDays(1);
        await database.SaveChangesAsync();
    }

    private static async Task VerifyClaimQueryLocksHierarchyAsync(
        string connectionString,
        Guid monitorId,
        Guid environmentId)
    {
        await using var claimConnection = new NpgsqlConnection(connectionString);
        await claimConnection.OpenAsync();
        await using var claimTransaction = await claimConnection.BeginTransactionAsync();
        await using (var claimCommand = new NpgsqlCommand(
            """
            SELECT monitor.id
            FROM web_health.endpoint_monitor AS monitor
            JOIN web_health.endpoint AS endpoint ON endpoint.id = monitor.endpoint_id
            JOIN web_health.environment AS environment ON environment.id = endpoint.environment_id
            JOIN web_health.website AS website ON website.id = environment.website_id
            JOIN web_health.client AS client ON client.id = website.client_id
            WHERE monitor.id = @monitor_id
            FOR UPDATE OF monitor, endpoint, environment, website, client SKIP LOCKED
            """,
            claimConnection,
            claimTransaction))
        {
            claimCommand.Parameters.AddWithValue("monitor_id", monitorId);
            await using var reader = await claimCommand.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue(
                "the claim query must lock the monitor row before a concurrent disable can race it");
        }

        await using var disableConnection = new NpgsqlConnection(connectionString);
        await disableConnection.OpenAsync();
        try
        {
            var disableTask = Task.Run(async () =>
            {
                await using var disableCommand = new NpgsqlCommand(
                    "UPDATE web_health.environment SET is_active = false WHERE id = @environment_id",
                    disableConnection);
                disableCommand.Parameters.AddWithValue("environment_id", environmentId);
                await disableCommand.ExecuteNonQueryAsync();
            });

            var raced = await Task.WhenAny(disableTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
            raced.Should().NotBe(disableTask,
                "the concurrent environment disable must block behind the claim query's row lock");

            await claimTransaction.CommitAsync();
            await disableTask;

            await using var verifyCommand = new NpgsqlCommand(
                "SELECT is_active FROM web_health.environment WHERE id = @id", disableConnection);
            verifyCommand.Parameters.AddWithValue("id", environmentId);
            (await verifyCommand.ExecuteScalarAsync()).Should().Be(false);
        }
        finally
        {
            await using var restoreCommand = new NpgsqlCommand(
                "UPDATE web_health.environment SET is_active = true WHERE id = @id", disableConnection);
            restoreCommand.Parameters.AddWithValue("id", environmentId);
            await restoreCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyManualChecksAndHistoryAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString,
            ["Monitoring:Scheduling:Enabled"] = "true"
        }).Build();
        var queue = new RecordingLogicalCheckQueue();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration)
            .AddSingleton<ILogicalCheckQueue>(queue)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var environmentService = scope.ServiceProvider.GetRequiredService<IEnvironmentRegistryService>();
        var endpointService = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();
        var manualCheckService = scope.ServiceProvider.GetRequiredService<IManualCheckService>();
        var historyReader = scope.ServiceProvider.GetRequiredService<ICheckHistoryReader>();

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var developer = await database.Users.SingleAsync(user => user.Email == "registry-developer@example.test");
        var viewer = await database.Users.SingleAsync(user => user.Email == "registry-viewer@example.test");
        var developerOwnerId = await database.OwnerSubjects.Where(owner => owner.UserId == developer.Id)
            .Select(owner => owner.Id).SingleAsync();
        var administratorOwnerId = await database.OwnerSubjects.Where(owner => owner.UserId == administrator.Id)
            .Select(owner => owner.Id).SingleAsync();
        var administratorAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var developerAccess = new RegistryAccessContext(developer.Id, [ApplicationRoles.DeveloperSupport]);
        var viewerAccess = new RegistryAccessContext(viewer.Id, [ApplicationRoles.Viewer]);

        var website = await database.Websites.SingleAsync(candidate =>
            candidate.Client.Name == "Second Client" && candidate.Name == "Portal");
        var environmentResult = await environmentService.CreateAsync(
            new(website.Id, "Manual Checks", EnvironmentTypes.Staging, "https://manual-checks.example.test", true),
            administratorAccess);
        environmentResult.Succeeded.Should().BeTrue(string.Join(" ", environmentResult.Errors));
        var environmentId = environmentResult.EntityId!.Value;

        var ownedResult = await endpointService.CreateAsync(
            new(environmentId, "HTTPS://Manual-Checks.EXAMPLE.test/Owned", developerOwnerId, true, null,
                TargetAuthorizationKinds.Owned, "Manual check integration fixture.", null),
            administratorAccess);
        ownedResult.Succeeded.Should().BeTrue(string.Join(" ", ownedResult.Errors));
        var ownedEndpointId = ownedResult.EntityId!.Value;

        var unownedResult = await endpointService.CreateAsync(
            new(environmentId, "https://manual-checks.example.test/unowned", administratorOwnerId, true, null,
                TargetAuthorizationKinds.Owned, "Manual check integration fixture.", null),
            administratorAccess);
        unownedResult.Succeeded.Should().BeTrue(string.Join(" ", unownedResult.Errors));
        var unownedEndpointId = unownedResult.EntityId!.Value;

        // An HTTPS endpoint owns a certificate monitor as well since SslCertificateMonitoring, so
        // the availability monitor has to be selected rather than assumed to be the only one.
        var ownedMonitor = await AvailabilityMonitors(database).AsNoTracking()
            .SingleAsync(candidate => candidate.EndpointId == ownedEndpointId);
        var nextDueBeforeAnyRun = ownedMonitor.NextDueAt;

        // Administrator and Operations can run now regardless of ownership.
        var adminRun = await manualCheckService.RunNowAsync(ownedEndpointId, administratorAccess);
        adminRun.Status.Should().Be(ManualCheckStatus.Queued);
        var adminUnownedRun = await manualCheckService.RunNowAsync(unownedEndpointId, administratorAccess);
        adminUnownedRun.Status.Should().Be(ManualCheckStatus.Queued);

        // Developer/Support can run now only for an assigned target with active testing evidence.
        var developerOwnedRun = await manualCheckService.RunNowAsync(ownedEndpointId, developerAccess);
        developerOwnedRun.Status.Should().Be(ManualCheckStatus.Queued);
        (await manualCheckService.RunNowAsync(unownedEndpointId, developerAccess)).Status
            .Should().Be(ManualCheckStatus.Forbidden);

        // Viewer is always denied, regardless of ownership.
        (await manualCheckService.RunNowAsync(ownedEndpointId, viewerAccess)).Status
            .Should().Be(ManualCheckStatus.Forbidden);
        (await manualCheckService.RunNowAsync(unownedEndpointId, viewerAccess)).Status
            .Should().Be(ManualCheckStatus.Forbidden);

        // Scheduled cadence is never touched by a manual run.
        (await database.EndpointMonitors.AsNoTracking()
            .Where(candidate => candidate.Id == ownedMonitor.Id)
            .Select(candidate => candidate.NextDueAt)
            .SingleAsync()).Should().Be(nextDueBeforeAnyRun);

        // Source, initiator, and queueing shape.
        var manualCheckId = developerOwnedRun.LogicalCheckId!.Value;
        var manualCheck = await database.LogicalChecks.AsNoTracking()
            .SingleAsync(check => check.Id == manualCheckId);
        manualCheck.Source.Should().Be(LogicalCheckSources.Manual);
        manualCheck.InitiatedByUserId.Should().Be(developer.Id);
        manualCheck.RequestedAt.Should().NotBeNull();
        manualCheck.ScheduledFor.Should().BeNull();
        manualCheck.CadenceKey.Should().BeNull();
        manualCheck.State.Should().Be(LogicalCheckStates.Queued);
        queue.Jobs.Should().Contain(job => job.LogicalCheckId == manualCheckId);
        var manualWork = await database.DurableWork.AsNoTracking()
            .SingleAsync(work => work.LogicalCheckId == manualCheckId);
        manualWork.State.Should().Be(DurableWorkStates.Enqueued);

        // Executing the manual check completes it without counting toward uptime.
        var manualTransport = new RecordingSafeHttpTransport(Success);
        var manualExecution = CreateExecutionService(database, manualTransport, true);
        (await manualExecution.ExecuteAsync(new(manualCheckId, manualWork.Id, "job-manual", "worker-a")))
            .Should().Be(LogicalCheckExecutionStatus.Completed);
        (await database.CheckResults.AsNoTracking().SingleAsync(result => result.LogicalCheckId == manualCheckId))
            .CountsForUptime.Should().BeFalse();

        // History and check detail are assignment-filtered exactly like the endpoint they belong to.
        (await historyReader.ListForEndpointAsync(ownedEndpointId, viewerAccess)).Should().BeNull();
        var historyPage = await historyReader.ListForEndpointAsync(ownedEndpointId, administratorAccess);
        historyPage.Should().NotBeNull();
        historyPage!.TotalCount.Should().Be(2);
        historyPage.EndpointDisplayUrl.Should().Be("HTTPS://Manual-Checks.EXAMPLE.test/Owned");
        historyPage.Items.Should().Contain(item => item.LogicalCheckId == manualCheckId
            && item.Source == LogicalCheckSources.Manual
            && item.InitiatedByDisplayName == developer.DisplayName
            && item.Outcome == HttpResultOutcomes.Healthy
            && item.CountsForUptime == false);

        (await historyReader.FindCheckAsync(manualCheckId, viewerAccess)).Should().BeNull();
        var details = await historyReader.FindCheckAsync(manualCheckId, administratorAccess);
        details.Should().NotBeNull();
        details!.Source.Should().Be(LogicalCheckSources.Manual);
        details.InitiatedByDisplayName.Should().Be(developer.DisplayName);
        details.CountsForUptime.Should().BeFalse();
        details.TotalDurationMs.Should().NotBeNull();
        details.EndpointDisplayUrl.Should().Be("HTTPS://Manual-Checks.EXAMPLE.test/Owned");

        // Pagination clamps out-of-range requests instead of overflowing or returning nonsense.
        (await historyReader.ListForEndpointAsync(ownedEndpointId, administratorAccess, page: 0))!
            .Page.Should().Be(1);
        var clampedToLastPage = await historyReader.ListForEndpointAsync(
            ownedEndpointId, administratorAccess, page: int.MaxValue);
        clampedToLastPage!.Page.Should().Be(1);
        clampedToLastPage.Items.Should().HaveCount(2);

        // The enqueue acknowledgement must never regress a durable work row that a racing worker
        // has already advanced past Dispatching, on a separate connection, before Enqueue returns.
        foreach (var racedState in new[] { DurableWorkStates.Processing, DurableWorkStates.Completed })
        {
            queue.OnEnqueue = (_, workId) =>
            {
                using var raceConnection = new NpgsqlConnection(connectionString);
                raceConnection.Open();
                using var raceCommand = new NpgsqlCommand(
                    "UPDATE web_health.durable_work SET state = @state, updated_at = now() WHERE id = @id",
                    raceConnection);
                raceCommand.Parameters.AddWithValue("state", racedState);
                raceCommand.Parameters.AddWithValue("id", workId);
                raceCommand.ExecuteNonQuery();
            };
            var racedRun = await manualCheckService.RunNowAsync(ownedEndpointId, administratorAccess);
            queue.OnEnqueue = null;
            racedRun.Status.Should().Be(ManualCheckStatus.Queued);
            (await database.DurableWork.AsNoTracking()
                .Where(work => work.LogicalCheckId == racedRun.LogicalCheckId)
                .Select(work => work.State)
                .SingleAsync()).Should().Be(racedState);
        }

        // Archived endpoints follow the same visibility rule as the rest of the registry: hidden
        // from non-managers, still reachable by Administrator/Operations. This must be last, since
        // it removes ownedEndpointId from developer/viewer visibility for anything that follows.
        var ownedEndpointVersion = await database.Endpoints.AsNoTracking()
            .Where(candidate => candidate.Id == ownedEndpointId)
            .Select(candidate => candidate.Version)
            .SingleAsync();
        (await endpointService.DeleteAsync(new(ownedEndpointId, ownedEndpointVersion), administratorAccess))
            .Succeeded.Should().BeTrue();
        (await historyReader.ListForEndpointAsync(ownedEndpointId, developerAccess)).Should().BeNull();
        (await historyReader.FindCheckAsync(manualCheckId, developerAccess)).Should().BeNull();
        (await historyReader.ListForEndpointAsync(ownedEndpointId, administratorAccess)).Should().NotBeNull();
        (await historyReader.FindCheckAsync(manualCheckId, administratorAccess)).Should().NotBeNull();
    }

    private static async Task VerifyManualChecksUnavailableWhenSchedulingDisabledAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString,
            ["Monitoring:Scheduling:Enabled"] = "false"
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        // IManualCheckService (and anything depending on it) must still activate when no Hangfire
        // queue is configured - the DI graph must not break registry pages that have nothing to do
        // with manual checks just because scheduling is administratively disabled.
        var manualCheckService = scope.ServiceProvider.GetRequiredService<IManualCheckService>();
        var targetReader = scope.ServiceProvider.GetRequiredService<ITargetRegistryReader>();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var administratorAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var endpointId = await database.Endpoints.AsNoTracking()
            .OrderBy(candidate => candidate.CreatedAt).ThenBy(candidate => candidate.Id)
            .Select(candidate => candidate.Id).FirstAsync();

        (await targetReader.FindEndpointAsync(endpointId, administratorAccess)).Should().NotBeNull();

        var checkCountBefore = await database.LogicalChecks.CountAsync();
        var result = await manualCheckService.RunNowAsync(endpointId, administratorAccess);
        result.Status.Should().Be(ManualCheckStatus.SchedulingUnavailable);
        result.LogicalCheckId.Should().BeNull();
        (await database.LogicalChecks.CountAsync()).Should().Be(checkCountBefore);
    }

    private static ServiceProvider BuildSchedulingServices(
        string connectionString,
        TimeProvider clock,
        ILogicalCheckQueue queue)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .AddSingleton(clock)
            .AddSingleton(queue)
            .BuildServiceProvider();
    }

    /// <summary>Item 22: migrating from the exact Phase 3 boundary applies the three Phase 4
    /// migrations cleanly on top of a database that already has real Phase 3 data in it.</summary>
    private static async Task VerifyPhaseThreeToPhaseFourUpgradeAsync(string connectionString)
    {
        var upgradeConnectionString = await CreateUpgradeDatabaseAsync(connectionString, "phase3");
        await using var services = BuildUpgradeServices(upgradeConnectionString);
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await database.Database.MigrateAsync("HangfireSchedulingAndRecovery");
        (await database.Database.GetAppliedMigrationsAsync()).Should().HaveCount(7);
        var phaseThreeState = await ReadFoundationState(upgradeConnectionString);
        phaseThreeState.Tables.Should().BeEquivalentTo(
            ExpectedTables.Except(TablesAddedAfterPhaseThree).Append(DatabaseConventions.MigrationsHistoryTable));

        await database.Database.MigrateAsync();
        (await database.Database.GetAppliedMigrationsAsync()).Should().HaveCount(ExpectedMigrations.Length);
        var upgradedState = await ReadFoundationState(upgradeConnectionString);
        upgradedState.Tables.Should().BeEquivalentTo(ExpectedTables.Append(DatabaseConventions.MigrationsHistoryTable));
    }

    /// <summary>
    /// Each upgrade check gets its own database in the same cluster. Walking the shared one
    /// backwards would require every intervening migration to reverse the data the features
    /// wrote — mapping severities and failure categories the older schema cannot express, and
    /// deleting the incidents that reference retired monitors — so the check would be asserting
    /// on the reversal rather than on the upgrade it exists to cover.
    /// </summary>
    private static async Task<string> CreateUpgradeDatabaseAsync(string connectionString, string suffix)
    {
        var target = $"{new NpgsqlConnectionStringBuilder(connectionString).Database}_{suffix}";
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{target}";""", connection);
            await drop.ExecuteNonQueryAsync();
            await using var create = new NpgsqlCommand($"""CREATE DATABASE "{target}";""", connection);
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(connectionString) { Database = target }.ConnectionString;
    }

    private static ServiceProvider BuildUpgradeServices(string connectionString) =>
        new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebHealth"] = connectionString,
                ["BootstrapAdmin:Email"] = "upgrade@example.test",
                ["BootstrapAdmin:DisplayName"] = "Upgrade Administrator",
                ["BootstrapAdmin:Password"] = $"Integration-9!{Guid.NewGuid():N}"
            }).Build())
            .BuildServiceProvider();

    /// <summary>
    /// Item 21: the Phase 2 migration derives a monitor's cadence anchor from its creation time,
    /// so the check needs a monitor that predates the migration. It is created at head through the
    /// registry so every column the current model requires is filled in, then the database is
    /// walked back to the Phase 1 boundary — which is safe here because this database holds no
    /// incident, certificate or SEO rows for the intervening down migrations to reverse.
    /// </summary>
    private static async Task VerifyPhaseTwoUpgradeAsync(string connectionString)
    {
        var upgradeConnectionString = await CreateUpgradeDatabaseAsync(connectionString, "phase2");
        await using var services = BuildUpgradeServices(upgradeConnectionString);
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await database.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().BootstrapAsync();

        await SeedUpgradeMonitorAsync(scope, database);

        database.ChangeTracker.Clear();
        await database.Database.MigrateAsync("RegistryFoundation");
        await database.Database.ExecuteSqlRawAsync(
            "UPDATE web_health.endpoint_monitor SET schedule_anchor = NULL, next_due_at = NULL");

        await database.Database.MigrateAsync();

        database.ChangeTracker.Clear();
        (await database.EndpointMonitors.AnyAsync()).Should().BeTrue(
            "the backfill is only evidence if a monitor predates the migration");
        (await database.EndpointMonitors.AnyAsync(monitor =>
            monitor.ScheduleAnchor != monitor.CreatedAt || monitor.NextDueAt != monitor.CreatedAt))
            .Should().BeFalse();
    }

    private static async Task SeedUpgradeMonitorAsync(AsyncServiceScope scope, ApplicationDbContext database)
    {
        var administrator = await database.Users.SingleAsync(user => user.Email == "upgrade@example.test");
        var ownerSubjectId = await database.OwnerSubjects
            .Where(owner => owner.UserId == administrator.Id)
            .Select(owner => owner.Id)
            .SingleAsync();
        var access = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var clientService = scope.ServiceProvider.GetRequiredService<IClientRegistryService>();
        var websiteService = scope.ServiceProvider.GetRequiredService<IWebsiteRegistryService>();
        var environmentService = scope.ServiceProvider.GetRequiredService<IEnvironmentRegistryService>();
        var endpointService = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();

        var client = await clientService.CreateAsync(new("Upgrade Client", ownerSubjectId, null), access);
        client.Succeeded.Should().BeTrue(string.Join(" ", client.Errors));
        // Created disabled: a website cannot be enabled until it has an active environment, and
        // the environment below is what this fixture is building up to.
        var website = await websiteService.CreateAsync(
            new(client.EntityId!.Value, "Upgrade Website", ownerSubjectId, null, false, []), access);
        website.Succeeded.Should().BeTrue(string.Join(" ", website.Errors));
        var environment = await environmentService.CreateAsync(
            new(website.EntityId!.Value, "Staging", EnvironmentTypes.Staging, null, true), access);
        environment.Succeeded.Should().BeTrue(string.Join(" ", environment.Errors));

        // Plain HTTP in a non-production environment: no certificate monitor to retire on the way
        // down and no production HTTP exception to approve.
        var endpoint = await endpointService.CreateAsync(
            new(environment.EntityId!.Value, "http://upgrade.test/status", null, true, null,
                TargetAuthorizationKinds.Owned, "Upgrade fixture owned by the project.", null),
            access);
        endpoint.Succeeded.Should().BeTrue(string.Join(" ", endpoint.Errors));
    }

    private static async Task VerifyPhaseOneUpgradeAndRepeatabilityAsync(string connectionString)
    {
        var upgradeConnectionString = await CreateUpgradeDatabaseAsync(connectionString, "phase1");
        await using var services = BuildUpgradeServices(upgradeConnectionString);
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await database.Database.MigrateAsync("InitialFoundation");
        (await database.Database.GetAppliedMigrationsAsync()).Should().ContainSingle();
        var baseline = await ReadFoundationState(upgradeConnectionString);
        baseline.Tables.Should().Equal(DatabaseConventions.MigrationsHistoryTable);

        await database.Database.MigrateAsync();
        var applied = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
        applied.Should().HaveCount(ExpectedMigrations.Length);
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        var upgraded = await ReadFoundationState(upgradeConnectionString);
        upgraded.Tables.Should().BeEquivalentTo(
            ExpectedTables.Append(DatabaseConventions.MigrationsHistoryTable));

        await database.Database.MigrateAsync();
        (await database.Database.GetAppliedMigrationsAsync()).Should().Equal(applied);
    }

    private static async Task VerifyEnvironmentTypeConstraintAsync(string connectionString, Guid environmentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.environment SET environment_type = 'Production', is_production = FALSE WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", environmentId);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.Should().Be("ck_environment_type_matches_production");
    }

    private static async Task VerifyProductionHttpConstraintAsync(
        string connectionString, Guid environmentId, Guid nonAdministratorId, Guid actorId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string sql = """
            INSERT INTO web_health.endpoint
                (id, environment_id, display_url, normalized_url, normalized_url_hash, normalized_host,
                 effective_port, normalization_version,
                 is_enabled, http_exception_reason, http_exception_approved_by_user_id, http_exception_approved_at,
                 created_at, created_by_user_id, updated_at, updated_by_user_id, version)
            VALUES (@id, @environment_id, 'http://unsafe.example.test/', 'http://unsafe.example.test/', @hash,
                    'unsafe.example.test', 80, 1,
                    TRUE, 'Not approved by an administrator', @approver, now(), now(), @actor, now(), @actor, 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("environment_id", environmentId);
        command.Parameters.AddWithValue("hash", new byte[32]);
        command.Parameters.AddWithValue("approver", nonAdministratorId);
        command.Parameters.AddWithValue("actor", actorId);
        await command.ExecuteNonQueryAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.ConstraintName.Should().Be("ck_production_http_endpoint_admin_exception");
    }

    private static async Task VerifyProductionHttpTransitionsAsync(
        string connectionString,
        Guid websiteId,
        Guid productionEnvironmentId,
        Guid nonAdministratorId,
        Guid actorId)
    {
        var nonProductionEnvironmentId = Guid.NewGuid();
        var httpEndpointId = Guid.NewGuid();
        var httpsEndpointId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            const string setupSql = """
                INSERT INTO web_health.environment
                    (id, website_id, name, normalized_name, normalization_version, environment_type,
                     is_production, is_active, created_at, created_by_user_id, updated_at, updated_by_user_id, version)
                VALUES (@environment_id, @website_id, 'Transition test', 'transition test', 1, 'Test',
                        FALSE, TRUE, now(), @actor, now(), @actor, 1);

                INSERT INTO web_health.endpoint
                    (id, environment_id, display_url, normalized_url, normalized_url_hash, normalized_host,
                     effective_port, normalization_version, is_enabled, http_exception_reason,
                     http_exception_approved_by_user_id, http_exception_approved_at,
                     created_at, created_by_user_id, updated_at, updated_by_user_id, version)
                VALUES (@http_endpoint_id, @environment_id, 'http://transition.example.test/',
                        'http://transition.example.test/', @http_hash, 'transition.example.test', 80, 1, TRUE,
                        'Evidence recorded before Production', @approver, now(), now(), @actor, now(), @actor, 1);

                INSERT INTO web_health.endpoint
                    (id, environment_id, display_url, normalized_url, normalized_url_hash, normalized_host,
                     effective_port, normalization_version, is_enabled, http_exception_reason,
                     http_exception_approved_by_user_id, http_exception_approved_at,
                     created_at, created_by_user_id, updated_at, updated_by_user_id, version)
                VALUES (@https_endpoint_id, @production_environment_id, 'https://scheme-change.example.test/',
                        'https://scheme-change.example.test/', @https_hash, 'scheme-change.example.test', 443, 1, TRUE,
                        'Irrelevant evidence while HTTPS', @approver, now(), now(), @actor, now(), @actor, 1);
                """;
            await using var setup = new NpgsqlCommand(setupSql, connection);
            setup.Parameters.AddWithValue("environment_id", nonProductionEnvironmentId);
            setup.Parameters.AddWithValue("website_id", websiteId);
            setup.Parameters.AddWithValue("http_endpoint_id", httpEndpointId);
            setup.Parameters.AddWithValue("https_endpoint_id", httpsEndpointId);
            setup.Parameters.AddWithValue("production_environment_id", productionEnvironmentId);
            setup.Parameters.AddWithValue("http_hash", Enumerable.Repeat((byte)1, 32).ToArray());
            setup.Parameters.AddWithValue("https_hash", Enumerable.Repeat((byte)2, 32).ToArray());
            setup.Parameters.AddWithValue("approver", nonAdministratorId);
            setup.Parameters.AddWithValue("actor", actorId);
            await setup.ExecuteNonQueryAsync();
        }

        await AssertProductionTransitionRejectedAsync(
            connectionString,
            "UPDATE web_health.environment SET environment_type = 'Production', is_production = TRUE WHERE id = @id",
            nonProductionEnvironmentId);
        await AssertProductionTransitionRejectedAsync(
            connectionString,
            "UPDATE web_health.endpoint SET environment_id = @production_id WHERE id = @id",
            httpEndpointId,
            productionEnvironmentId);
        await AssertProductionTransitionRejectedAsync(
            connectionString,
            """
                UPDATE web_health.endpoint
                SET display_url = 'http://scheme-change.example.test/',
                    normalized_url = 'http://scheme-change.example.test/',
                    normalized_url_hash = @hash,
                    effective_port = 80
                WHERE id = @id
                """,
            httpsEndpointId,
            hash: Enumerable.Repeat((byte)3, 32).ToArray());
    }

    private static async Task AssertProductionTransitionRejectedAsync(
        string connectionString,
        string sql,
        Guid endpointOrEnvironmentId,
        Guid? productionEnvironmentId = null,
        byte[]? hash = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", endpointOrEnvironmentId);
        if (productionEnvironmentId is not null)
        {
            command.Parameters.AddWithValue("production_id", productionEnvironmentId.Value);
        }

        if (hash is not null)
        {
            command.Parameters.AddWithValue("hash", hash);
        }

        await command.ExecuteNonQueryAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.ConstraintName.Should().Be("ck_production_http_endpoint_admin_exception");
    }

    private static async Task VerifyMonitorPolicyConstraintAsync(string connectionString, Guid endpointId, Guid actorId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // Since SslCertificateMonitoring, an HTTPS endpoint already owns a live certificate
        // monitor, and ix_endpoint_monitor_endpoint_id_monitor_type would reject this row on
        // INSERT before the deferred policy trigger under test could run at COMMIT. That partial
        // index covers live monitors only, so the row is written already soft-deleted: it is the
        // type-versus-policy pairing that must be rejected here, not uniqueness.
        const string sql = """
            INSERT INTO web_health.endpoint_monitor
                (id, endpoint_id, policy_profile_id, monitor_type, bounded_overrides, configuration_fingerprint,
                 interval_seconds, timeout_seconds, failure_confirmation_count, recovery_confirmation_count,
                 schedule_anchor, next_due_at, is_enabled,
                 created_at, created_by_user_id, updated_at, updated_by_user_id, deleted_at, version)
            VALUES (@id, @endpoint_id, 'fd3c8021-ff54-4f31-a3ad-2010b7b193dd', 'SslCertificate', '{}', repeat('0', 64),
                    900, 30, 2, 2, now(), now(), FALSE, now(), @actor, now(), @actor, now(), 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("endpoint_id", endpointId);
        command.Parameters.AddWithValue("actor", actorId);
        await command.ExecuteNonQueryAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.ConstraintName.Should().Be("ck_endpoint_monitor_policy_type");
    }

    private static async Task VerifyHealthMaintenanceAndIncidentsAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The earliest monitor overall may now be a certificate monitor, which never produces
        // logical checks of the kind this assertion needs (SslCertificateMonitoring).
        var monitor = await AvailabilityMonitors(database)
            .OrderBy(candidate => candidate.CreatedAt).FirstAsync();
        var otherMonitorId = await database.EndpointMonitors
            .Where(candidate => candidate.Id != monitor.Id)
            .OrderBy(candidate => candidate.CreatedAt).ThenBy(candidate => candidate.Id)
            .Select(candidate => candidate.Id)
            .FirstAsync();
        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var developer = await database.Users.SingleAsync(user => user.Email == "registry-developer@example.test");
        var ownerSubjectId = await database.OwnerSubjects
            .Where(subject => subject.UserId == developer.Id)
            .Select(subject => subject.Id)
            .SingleAsync();
        var userId = administrator.Id;
        var now = DateTimeOffset.UtcNow;
        const string issueKey = "v1|HttpAvailability|status-code|default";

        var maintenanceService = scope.ServiceProvider.GetRequiredService<IMaintenanceWindowService>();
        var maintenanceEvaluator = scope.ServiceProvider.GetRequiredService<IMaintenanceEvaluator>();
        var maintenanceAccess = new RegistryAccessContext(userId, [ApplicationRoles.Administrator]);
        var createdMaintenance = await maintenanceService.CreateAsync(new(
            new(MaintenanceScopeKind.Monitor, monitor.Id), now.AddMinutes(-5), now.AddMinutes(5), "UTC",
            "Controlled maintenance verification", MaintenanceSuppressionPolicies.SuppressAll, true, false, OneOff),
            maintenanceAccess);
        createdMaintenance.Succeeded.Should().BeTrue(string.Join(" ", createdMaintenance.Errors));
        var activeMaintenance = await maintenanceEvaluator.FindActiveAsync(monitor.Id, now);
        activeMaintenance.Should().NotBeNull();
        activeMaintenance!.SuppressionPolicy.Should().Be(MaintenanceSuppressionPolicies.SuppressAll);
        (await database.AuditEvents.AnyAsync(eventRecord => eventRecord.Action == "maintenance.created"
            && eventRecord.EntityIdentifier == createdMaintenance.MaintenanceWindowId!.Value.ToString())).Should().BeTrue();
        (await maintenanceService.CancelAsync(new(createdMaintenance.MaintenanceWindowId!.Value, 1), maintenanceAccess))
            .Succeeded.Should().BeTrue();
        (await maintenanceEvaluator.FindActiveAsync(monitor.Id, now)).Should().BeNull();

        database.IssueStates.Add(new IssueState
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = monitor.Id,
            IssueKey = issueKey,
            ConsecutiveFailures = 1,
            ConsecutiveRecoveries = 0,
            UpdatedAt = now
        });
        await database.SaveChangesAsync();
        await VerifyDuplicateIssueStateRejectedAsync(connectionString, monitor.Id, issueKey);

        if (!await database.EndpointHealth.AnyAsync(health => health.EndpointMonitorId == monitor.Id))
        {
            database.EndpointHealth.Add(new EndpointHealth
            {
                EndpointMonitorId = monitor.Id,
                ConfirmedStatus = EndpointHealthStatuses.Healthy,
                ConfirmedAt = now,
                Version = 1
            });
            await database.SaveChangesAsync();
        }
        await VerifyEndpointHealthCrossMonitorRejectedAsync(connectionString, monitor.Id, otherMonitorId);

        var incidentId = Guid.NewGuid();
        database.Incidents.Add(new Incident
        {
            Id = incidentId,
            EndpointMonitorId = monitor.Id,
            OwnerSubjectId = ownerSubjectId,
            IssueKey = issueKey,
            Severity = IncidentSeverities.Critical,
            Status = IncidentStatuses.Open,
            OpenedAt = now
        });
        await database.SaveChangesAsync();

        await VerifyDuplicateActiveIncidentRejectedAsync(connectionString, monitor.Id, ownerSubjectId, issueKey);
        await VerifyIncidentResolutionFieldsRejectedAsync(connectionString, monitor.Id, ownerSubjectId);
        await VerifyIncidentAcknowledgedFieldExactnessRejectedAsync(connectionString, monitor.Id, ownerSubjectId);
        await VerifyIncidentClosedFieldExactnessRejectedAsync(connectionString, monitor.Id, ownerSubjectId);

        var trackedIncident = await database.Incidents.SingleAsync(incident => incident.Id == incidentId);
        trackedIncident.Status = IncidentStatuses.Acknowledged;
        trackedIncident.AcknowledgedAt = now;
        trackedIncident.Version++;

        await using (var conflictingScope = services.CreateAsyncScope())
        {
            var conflictingDatabase = conflictingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var conflictingIncident = await conflictingDatabase.Incidents.SingleAsync(incident => incident.Id == incidentId);
            conflictingIncident.Status = IncidentStatuses.Acknowledged;
            conflictingIncident.AcknowledgedAt = now;
            conflictingIncident.Version++;
            await conflictingDatabase.SaveChangesAsync();
        }

        var conflictingIncidentUpdate = async () => await database.SaveChangesAsync();
        await conflictingIncidentUpdate.Should().ThrowAsync<DbUpdateConcurrencyException>();
        database.ChangeTracker.Clear();

        var lifecycle = scope.ServiceProvider.GetRequiredService<IIncidentLifecycleService>();
        var developerAccess = new RegistryAccessContext(developer.Id, [ApplicationRoles.DeveloperSupport]);
        var administratorAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var operationsAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Operations]);
        (await lifecycle.CloseAsync(new(incidentId, 1), developerAccess)).Status
            .Should().Be(IncidentMutationStatus.ValidationFailed);
        (await lifecycle.StartProgressAsync(new(incidentId, 1), developerAccess)).Succeeded.Should().BeTrue();

        // Role/assignment matrix: Viewer is read-only and an unassigned Developer/Support user
        // has no claim on this incident's owner subject — both must be rejected, and rejection
        // must not consume the optimistic-concurrency version (the next call still uses version 2).
        var viewerAccess = new RegistryAccessContext(Guid.NewGuid(), [ApplicationRoles.Viewer]);
        (await lifecycle.AcknowledgeAsync(new(incidentId, 2), viewerAccess)).Status
            .Should().Be(IncidentMutationStatus.Forbidden);
        (await lifecycle.AddNoteAsync(new(incidentId, 2, "A viewer must never manage an incident."), viewerAccess)).Status
            .Should().Be(IncidentMutationStatus.Forbidden);
        var unassignedDeveloperAccess = new RegistryAccessContext(Guid.NewGuid(), [ApplicationRoles.DeveloperSupport]);
        (await lifecycle.AddNoteAsync(
            new(incidentId, 2, "An unassigned Developer/Support user must not manage this incident."),
            unassignedDeveloperAccess)).Status
            .Should().Be(IncidentMutationStatus.Forbidden);

        (await lifecycle.ResolveAsync(new(incidentId, 2, "", ""), developerAccess)).Status
            .Should().Be(IncidentMutationStatus.ValidationFailed);
        (await lifecycle.ResolveAsync(new(incidentId, 2, "Remediated", "The assigned developer confirmed the fix."), developerAccess))
            .Succeeded.Should().BeTrue();
        (await lifecycle.CloseAsync(new(incidentId, 3), developerAccess)).Succeeded.Should().BeTrue();
        (await lifecycle.ReopenAsync(new(incidentId, 4, "Needs another review."), operationsAccess)).Status
            .Should().Be(IncidentMutationStatus.Forbidden);
        (await lifecycle.ReopenAsync(new(incidentId, 4, "Needs another review."), administratorAccess))
            .Succeeded.Should().BeTrue();
        (await lifecycle.AddNoteAsync(new(incidentId, 5, "Reopened for controlled verification."), developerAccess))
            .Succeeded.Should().BeTrue();
        var reassignedOwnerId = await database.OwnerSubjects
            // Deterministic and still assignable: earlier stages disable users and teams, and
            // reassignment rejects an owner whose user or team is no longer enabled.
            .Where(subject => subject.Id != ownerSubjectId
                && ((subject.UserId != null
                        && database.Users.Any(user => user.Id == subject.UserId && !user.IsDisabled))
                    || (subject.TeamId != null
                        && database.Teams.Any(team => team.Id == subject.TeamId && !team.IsDisabled))))
            .OrderBy(subject => subject.Id)
            .Select(subject => subject.Id)
            .FirstAsync();
        (await lifecycle.ReassignAsync(new(incidentId, 6, reassignedOwnerId), administratorAccess))
            .Succeeded.Should().BeTrue();
        (await lifecycle.ForceCloseAsync(new(incidentId, 7, "Controlled administrative closure."), operationsAccess)).Status
            .Should().Be(IncidentMutationStatus.Forbidden);
        (await lifecycle.ForceCloseAsync(new(incidentId, 7, "Controlled administrative closure."), administratorAccess))
            .Succeeded.Should().BeTrue();

        var lifecycleIncident = await database.Incidents.SingleAsync(incident => incident.Id == incidentId);
        lifecycleIncident.Status.Should().Be(IncidentStatuses.Closed);
        lifecycleIncident.ResolutionCategory.Should().Be(IncidentResolutionCategories.ForcedClosure);
        (await database.IncidentEvents.CountAsync(eventRecord => eventRecord.IncidentId == incidentId))
            .Should().Be(7);
        (await database.IncidentEvidence.AnyAsync(evidence => evidence.IncidentId == incidentId
            && evidence.EvidenceType == IncidentEvidenceTypes.Resolution
            && evidence.ActorUserId == developer.Id)).Should().BeTrue();
        (await database.AuditEvents.CountAsync(audit => audit.EntityIdentifier == incidentId.ToString()
            && audit.Action.StartsWith("incident."))).Should().Be(7);

        await VerifyDuplicateIncidentEventSequenceRejectedAsync(connectionString, incidentId);
        await VerifyIncidentEventImmutableAsync(connectionString, incidentId);
        await VerifyIncidentEventFieldsRejectedAsync(connectionString, incidentId);

        var evidenceLogicalCheckId = await database.LogicalChecks
            .Where(check => check.EndpointMonitorId == monitor.Id)
            .OrderBy(check => check.CreatedAt).ThenBy(check => check.Id)
            .Select(check => check.Id)
            .FirstAsync();
        await VerifyIncidentEvidenceCrossMonitorRejectedAsync(
            connectionString, incidentId, monitor.Id, otherMonitorId);

        var evidenceId = Guid.NewGuid();
        database.IncidentEvidence.Add(new IncidentEvidence
        {
            Id = evidenceId,
            IncidentId = incidentId,
            EndpointMonitorId = monitor.Id,
            LogicalCheckId = evidenceLogicalCheckId,
            EvidenceType = IncidentEvidenceTypes.Opening,
            EvidenceRole = "CheckResult",
            BoundedSnapshot = "{}",
            CapturedAt = now
        });
        await database.SaveChangesAsync();
        await VerifyIncidentEvidenceImmutableAsync(connectionString, evidenceId);

        var windowId = Guid.NewGuid();
        database.MaintenanceWindows.Add(new MaintenanceWindow
        {
            Id = windowId,
            CreatedByUserId = userId,
            Reason = "Scheduled patching",
            TimezoneId = "UTC",
            SuppressionPolicy = MaintenanceSuppressionPolicies.SuppressAll,
            ScheduleStartsAt = now.AddHours(1),
            ScheduleDurationSeconds = 7200,
            RecurrencePattern = MaintenanceRecurrencePatterns.None,
            PauseEscalation = true,
            ContinueFailureCounter = false,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = userId
        });
        await database.SaveChangesAsync();
        await VerifyMaintenanceTargetScopeRejectedAsync(connectionString, windowId, monitor.Id);

        database.MaintenanceTargets.Add(new MaintenanceTarget
        {
            Id = Guid.NewGuid(),
            MaintenanceWindowId = windowId,
            EndpointMonitorId = monitor.Id
        });
        var occurrenceId = Guid.NewGuid();
        var startsAt = now.AddHours(1);
        database.MaintenanceOccurrences.Add(new MaintenanceOccurrence
        {
            Id = occurrenceId,
            MaintenanceWindowId = windowId,
            StartsAt = startsAt,
            EndsAt = startsAt.AddHours(2),
            CreatedAt = now
        });
        await database.SaveChangesAsync();
        await VerifyMaintenanceOccurrenceIntervalRejectedAsync(connectionString, windowId);
        await VerifyCheckResultMaintenanceFieldGroupRejectedAsync(connectionString, occurrenceId);
        await VerifyCheckResultMaintenanceIntervalRejectedAsync(connectionString, occurrenceId);
        await VerifyMaintenanceOccurrenceImmutableAsync(connectionString, occurrenceId);

        var trackedWindow = await database.MaintenanceWindows.SingleAsync(window => window.Id == windowId);
        trackedWindow.Reason = "Extended patching";
        trackedWindow.UpdatedAt = now;
        trackedWindow.Version++;

        await using (var conflictingScope = services.CreateAsyncScope())
        {
            var conflictingDatabase = conflictingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var conflictingWindow = await conflictingDatabase.MaintenanceWindows.SingleAsync(window => window.Id == windowId);
            conflictingWindow.Reason = "Emergency patching";
            conflictingWindow.UpdatedAt = now;
            conflictingWindow.Version++;
            await conflictingDatabase.SaveChangesAsync();
        }

        var conflictingWindowUpdate = async () => await database.SaveChangesAsync();
        await conflictingWindowUpdate.Should().ThrowAsync<DbUpdateConcurrencyException>();
        database.ChangeTracker.Clear();
    }

    private static async Task VerifyDurableNotificationsAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transport = (RecordingEmailTransport)scope.ServiceProvider.GetRequiredService<IEmailTransport>();
        var dispatchService = scope.ServiceProvider.GetRequiredService<NotificationDispatchService>();

        var monitor = await database.EndpointMonitors
            .Include(candidate => candidate.Endpoint)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstAsync();
        var developer = await database.Users.SingleAsync(user => user.Email == "registry-developer@example.test");
        var ownerSubjectId = await database.OwnerSubjects
            .Where(subject => subject.UserId == developer.Id)
            .Select(subject => subject.Id)
            .SingleAsync();
        var now = DateTimeOffset.UtcNow;

        var (incident, openedEvent) = await CreateOpenIncidentAsync(
            database, monitor.Id, ownerSubjectId, $"v1|HttpAvailability|notification-open|{Guid.NewGuid():N}", now);

        var writer = new NotificationEventWriter(database);
        await writer.WriteAsync(
            incident,
            openedEvent.Id,
            NotificationSourceKinds.IncidentEvent,
            NotificationEventTypes.Opened,
            NotificationOccurrenceKeys.Opening(incident.Id),
            isMaintenance: false,
            now,
            default);
        await database.SaveChangesAsync();

        var notificationEvent = await database.NotificationEvents.AsNoTracking()
            .SingleAsync(candidate => candidate.IncidentId == incident.Id);
        notificationEvent.IsSuppressed.Should().BeFalse();
        var delivery = await database.NotificationDeliveries.AsNoTracking()
            .SingleAsync(candidate => candidate.NotificationEventId == notificationEvent.Id);
        delivery.NormalizedRecipient.Should().Be("registry-developer@example.test");
        delivery.State.Should().Be(NotificationDeliveryStates.Pending);

        await VerifyDuplicateNotificationEventRejectedAsync(
            connectionString, incident.Id, openedEvent.Id, notificationEvent.OccurrenceKey);

        var dispatchResult = await dispatchService.DispatchDueAsync();
        dispatchResult.Sent.Should().BeGreaterThanOrEqualTo(1);
        transport.SentMessages.Should().Contain(message =>
            message.ToAddress == "registry-developer@example.test"
            && message.Subject.Contains(monitor.Endpoint.DisplayUrl, StringComparison.Ordinal));
        database.ChangeTracker.Clear();
        var sentDelivery = await database.NotificationDeliveries.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == delivery.Id);
        sentDelivery.State.Should().Be(NotificationDeliveryStates.Sent);
        sentDelivery.SentAt.Should().NotBeNull();
        var sentAttempt = await database.NotificationAttempts.AsNoTracking()
            .SingleAsync(attempt => attempt.NotificationDeliveryId == delivery.Id);
        sentAttempt.TransportOutcome.Should().Be(NotificationTransportOutcomes.Sent);

        // Item 20: the persisted transport-response text must never carry the recipient address
        // or any other sensitive value, regardless of what a real transport might return.
        sentAttempt.SafeResponse.Should().NotContain("@");
        sentAttempt.SafeResponse.Should().NotContain(delivery.NormalizedRecipient);

        // Item 10: a delivery left mid-dispatch by a crashed worker (Processing, lease expired)
        // must be reclaimed by the next tick and sent exactly once — no duplicate attempt, no
        // permanent loss. The claim query unions this case with ordinary due deliveries, so a
        // second DispatchDueAsync call is the entire restart-reconciliation mechanism.
        var (staleIncident, staleEvent) = await CreateOpenIncidentAsync(
            database, monitor.Id, ownerSubjectId, $"v1|HttpAvailability|notification-stale-lease|{Guid.NewGuid():N}", now);
        await writer.WriteAsync(
            staleIncident, staleEvent.Id, NotificationSourceKinds.IncidentEvent, NotificationEventTypes.Opened,
            NotificationOccurrenceKeys.Opening(staleIncident.Id), isMaintenance: false, now, default);
        await database.SaveChangesAsync();
        var staleDeliveryId = await database.NotificationDeliveries.AsNoTracking()
            .Where(candidate => candidate.NotificationEvent.IncidentId == staleIncident.Id)
            .Select(candidate => candidate.Id)
            .SingleAsync();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                UPDATE web_health.notification_delivery
                SET state = 'Processing', lease_owner = 'crashed-worker', lease_expires_at = now() - interval '1 minute'
                WHERE id = @id
                """,
                connection);
            command.Parameters.AddWithValue("id", staleDeliveryId);
            await command.ExecuteNonQueryAsync();
        }

        var reconciliationResult = await dispatchService.DispatchDueAsync();
        reconciliationResult.Claimed.Should().BeGreaterThanOrEqualTo(1);
        database.ChangeTracker.Clear();
        var reclaimedDelivery = await database.NotificationDeliveries.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == staleDeliveryId);
        reclaimedDelivery.State.Should().Be(NotificationDeliveryStates.Sent);
        (await database.NotificationAttempts.AsNoTracking()
            .CountAsync(attempt => attempt.NotificationDeliveryId == staleDeliveryId)).Should().Be(1);

        var (maintenanceIncident, maintenanceEvent) = await CreateOpenIncidentAsync(
            database, monitor.Id, ownerSubjectId, $"v1|HttpAvailability|notification-suppressed|{Guid.NewGuid():N}", now);
        await writer.WriteAsync(
            maintenanceIncident,
            maintenanceEvent.Id,
            NotificationSourceKinds.IncidentEvent,
            NotificationEventTypes.Opened,
            NotificationOccurrenceKeys.Opening(maintenanceIncident.Id),
            isMaintenance: true,
            now,
            default);
        await database.SaveChangesAsync();
        var suppressedDelivery = await database.NotificationDeliveries.AsNoTracking()
            .SingleAsync(candidate => candidate.NotificationEvent.IncidentId == maintenanceIncident.Id);
        suppressedDelivery.State.Should().Be(NotificationDeliveryStates.Suppressed);
        suppressedDelivery.NextAttemptAt.Should().BeNull();
        (await database.NotificationEvents.AsNoTracking()
            .Where(candidate => candidate.IncidentId == maintenanceIncident.Id)
            .Select(candidate => candidate.SuppressionReason)
            .SingleAsync()).Should().Be("ActiveMaintenanceWindow");

        await VerifyTransientFailureRetriesThenFailsPermanentlyAsync(connectionString, database, monitor.Id, ownerSubjectId);

        // These incidents stay Open/Critical/unacknowledged by design (that's what each assertion
        // above needed); leaving them that way would make them look like unacknowledged critical
        // incidents to the reminder/escalation sweep test that runs later and asserts exact
        // system-wide counts, so close them out here rather than leaking test-fixture state.
        await AcknowledgeAndResolveAsync(database, incident.Id);
        await AcknowledgeAndResolveAsync(database, staleIncident.Id);
        await AcknowledgeAndResolveAsync(database, maintenanceIncident.Id);
    }

    /// <summary>
    /// Items 11, 12 and 14. The candidate pool assumes no other Critical/active/unacknowledged
    /// incidents exist system-wide at the moment this runs — every earlier test that leaves one
    /// behind acknowledges/resolves it before returning (see AcknowledgeAndResolveAsync), so this
    /// can assert exact counts without an unrelated incident's own timer crossing a boundary too.
    /// </summary>
    private static async Task VerifyReminderEscalationSweepBoundariesAsync(string connectionString)
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton<TimeProvider>(clock)
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reminderService = scope.ServiceProvider.GetRequiredService<NotificationReminderService>();
        var maintenanceService = scope.ServiceProvider.GetRequiredService<IMaintenanceWindowService>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IIncidentLifecycleService>();

        var monitor = await CreateOwnedMonitorAsync(scope, database, "http://reminder-escalation.test/status");

        // Earlier finalize calls run against production/HTTP monitors incidentally accumulate an
        // unrelated "Http.HttpsRequired" issue-state counter alongside whichever issue key each
        // test intended to exercise (RequiresHttpsFinding fires for every successful transport
        // result on a production endpoint whose final destination isn't HTTPS). That can silently
        // open its own incident that no earlier test's targeted cleanup ever touches. This sweep
        // is the last thing that runs before the exact-count assertions below, so it closes out
        // every straggler regardless of source rather than chasing each origin individually.
        await CloseAllActiveIncidentsAsync(database);
        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var developer = await database.Users.SingleAsync(user => user.Email == "registry-developer@example.test");
        var ownerSubjectId = await database.OwnerSubjects
            .Where(subject => subject.UserId == developer.Id)
            .Select(subject => subject.Id)
            .SingleAsync();
        var administratorAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var developerAccess = new RegistryAccessContext(developer.Id, [ApplicationRoles.DeveloperSupport]);

        var (incident, _) = await CreateOpenIncidentAsync(
            database, monitor.Id, ownerSubjectId, $"v1|HttpAvailability|reminder-sweep|{Guid.NewGuid():N}", clock.GetUtcNow());

        (await reminderService.SweepAsync()).Should().Be(new NotificationReminderSweepResult(0, 0));

        // Item 14: an active maintenance window pauses both, even once the incident is well past
        // the escalation boundary.
        clock.Advance(TimeSpan.FromMinutes(31));
        var maintenanceWindow = await maintenanceService.CreateAsync(new(
            new(MaintenanceScopeKind.Monitor, monitor.Id),
            clock.GetUtcNow().AddMinutes(-1),
            clock.GetUtcNow().AddHours(1),
            "UTC",
            "Reminder sweep pause verification",
            MaintenanceSuppressionPolicies.SuppressAll,
            true,
            false,
            OneOff), administratorAccess);
        maintenanceWindow.Succeeded.Should().BeTrue(string.Join(" ", maintenanceWindow.Errors));
        (await reminderService.SweepAsync()).Should().Be(new NotificationReminderSweepResult(0, 0));
        (await maintenanceService.CancelAsync(new(maintenanceWindow.MaintenanceWindowId!.Value, 1), administratorAccess))
            .Succeeded.Should().BeTrue();

        // Item 11 (escalation boundary): 31 minutes unacknowledged crosses the 30-minute escalation
        // delay but not the 60-minute reminder interval.
        var escalationResult = await reminderService.SweepAsync();
        escalationResult.EscalationsWritten.Should().Be(1);
        escalationResult.RemindersWritten.Should().Be(0);
        (await database.NotificationEvents.CountAsync(notificationEvent =>
            notificationEvent.IncidentId == incident.Id && notificationEvent.EventType == NotificationEventTypes.Escalated))
            .Should().Be(1);

        // Same elapsed time again: the deterministic single-level occurrence key makes a second
        // escalation attempt an idempotent no-op, not a duplicate.
        (await reminderService.SweepAsync()).EscalationsWritten.Should().Be(0);

        // Item 11 (reminder boundary): advancing past 60 minutes total fires exactly one reminder.
        clock.Advance(TimeSpan.FromMinutes(30));
        var reminderResult = await reminderService.SweepAsync();
        reminderResult.RemindersWritten.Should().Be(1);
        (await database.NotificationEvents.CountAsync(notificationEvent =>
            notificationEvent.IncidentId == incident.Id && notificationEvent.EventType == NotificationEventTypes.Reminder))
            .Should().Be(1);

        // Item 12: acknowledging stops both, permanently, regardless of how much further time passes.
        var trackedIncident = await database.Incidents.SingleAsync(candidate => candidate.Id == incident.Id);
        (await lifecycle.AcknowledgeAsync(new(incident.Id, trackedIncident.Version), developerAccess))
            .Succeeded.Should().BeTrue();
        clock.Advance(TimeSpan.FromHours(2));
        (await reminderService.SweepAsync()).Should().Be(new NotificationReminderSweepResult(0, 0));
    }

    /// <summary>
    /// Item 13 (retention half): a check finalized during an active maintenance window is marked
    /// and kept, not dropped, through the real FinalizeAsync pipeline — not a hand-built row.
    /// </summary>
    /// <summary>
    /// BR-M05 and the AC-09 regression. Expansion is keyed on (window, occurrence start): running
    /// it twice over one horizon writes nothing, extending the horizon appends only later
    /// occurrences and rewrites no history, and a failure inside a materialised recurring
    /// occurrence is retained and suppressed exactly as a one-off window's is.
    /// </summary>
    private static async Task VerifyRecurringMaintenanceExpansionAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var maintenanceService = scope.ServiceProvider.GetRequiredService<IMaintenanceWindowService>();
        var maintenanceEvaluator = scope.ServiceProvider.GetRequiredService<IMaintenanceEvaluator>();
        var endpointService = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();
        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var access = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        // Ordered so the environment this stage builds on is the same one every run: an unordered
        // First leaves production-ness, and everything keyed off it, to the query planner.
        var environmentId = await database.Environments
            .Where(candidate => candidate.DeletedAt == null)
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => candidate.Id).FirstAsync();

        var endpointResult = await endpointService.CreateAsync(
            new(environmentId, "https://recurring-maintenance.test/status", null, true, null,
                TargetAuthorizationKinds.Owned, "Recurring maintenance fixture owned by the project.", null),
            access);
        endpointResult.Succeeded.Should().BeTrue(string.Join(" ", endpointResult.Errors));
        var monitor = await database.EndpointMonitors
            .Include(candidate => candidate.Endpoint).ThenInclude(endpoint => endpoint.Environment)
            .SingleAsync(candidate => candidate.EndpointId == endpointResult.EntityId!.Value
                && candidate.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType
                && candidate.DeletedAt == null);

        const int horizonDays = 90;
        var options = new MaintenanceSchedulingOptions { HorizonDays = horizonDays, BatchSize = 25 };
        // timestamptz keeps microseconds while a .NET tick is 100 nanoseconds, so a clock started
        // on an unrounded UtcNow produces timestamps that come back from the round-trip a digit
        // short, failing the comparisons below on every run except the one in ten that happens to
        // land on a whole microsecond. Truncating once here keeps every value derived from it exact.
        var startedAt = DateTimeOffset.UtcNow;
        var clock = new MutableTimeProvider(new DateTimeOffset(
            startedAt.Ticks - (startedAt.Ticks % TimeSpan.TicksPerMicrosecond),
            startedAt.Offset));
        var anchor = clock.GetUtcNow().AddMinutes(-5);
        var created = await maintenanceService.CreateAsync(new(
            new(MaintenanceScopeKind.Monitor, monitor.Id),
            anchor,
            anchor.AddHours(1),
            "Europe/Berlin",
            "Nightly recurring maintenance verification",
            MaintenanceSuppressionPolicies.SuppressAll,
            true,
            false,
            new(MaintenanceRecurrencePatterns.Daily, MaintenanceDayOfWeekMask.Empty, null)), access);
        created.Succeeded.Should().BeTrue(string.Join(" ", created.Errors));
        var windowId = created.MaintenanceWindowId!.Value;

        // Creation materialises the whole first horizon in its own transaction, so the window
        // suppresses immediately rather than from the next expansion tick.
        var initialStarts = await OccurrenceStartsAsync(database, windowId);
        // One per local day across the horizon. The last one can fall an hour past the horizon
        // when the range crosses a daylight-saving transition, which is the wall-clock rule
        // working, so the count is the horizon or one more.
        initialStarts.Should().HaveCountGreaterThanOrEqualTo(horizonDays)
            .And.HaveCountLessThanOrEqualTo(horizonDays + 1);
        initialStarts[0].Should().Be(anchor.ToUniversalTime());
        initialStarts.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();

        var expander = new MaintenanceOccurrenceExpander(database, options, clock, NullLogger<MaintenanceOccurrenceExpander>.Instance);
        (await expander.ExpandWindowAsync(windowId)).Should().Be(0,
            "re-running the expander over the same horizon cannot double-book a window");
        database.ChangeTracker.Clear();
        (await expander.ExpandDueAsync()).OccurrencesCreated.Should().Be(0);
        database.ChangeTracker.Clear();
        (await OccurrenceStartsAsync(database, windowId)).Should().Equal(initialStarts);

        // AC-09 regression against a materialised recurring occurrence.
        var activeOccurrence = await maintenanceEvaluator.FindActiveAsync(monitor.Id, clock.GetUtcNow());
        activeOccurrence.Should().NotBeNull();
        activeOccurrence!.SuppressionPolicy.Should().Be(MaintenanceSuppressionPolicies.SuppressAll);
        var checkId = await FinalizeScheduledResultAsync(database, monitor, 500, clock);
        database.ChangeTracker.Clear();
        var result = await database.CheckResults.AsNoTracking()
            .SingleAsync(candidate => candidate.LogicalCheckId == checkId);
        result.IsMaintenance.Should().BeTrue();
        result.MaintenanceOccurrenceId.Should().Be(activeOccurrence.OccurrenceId);
        result.CountsForUptime.Should().BeFalse();
        (await database.IssueStates.AnyAsync(state => state.EndpointMonitorId == monitor.Id)).Should().BeFalse();
        (await database.Incidents.AnyAsync(incident => incident.EndpointMonitorId == monitor.Id)).Should().BeFalse();

        // Extending the horizon appends only later occurrences and rewrites no history.
        clock.Advance(TimeSpan.FromDays(10));
        database.ChangeTracker.Clear();
        var appended = await expander.ExpandWindowAsync(windowId);
        appended.Should().BeInRange(10, 11, "ten more local days, give or take a transition day");
        database.ChangeTracker.Clear();
        var extendedStarts = await OccurrenceStartsAsync(database, windowId);
        extendedStarts.Should().HaveCount(initialStarts.Count + appended);
        extendedStarts.Take(initialStarts.Count).Should().Equal(initialStarts,
            "extending the horizon appends and never rewrites history");
        extendedStarts.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        var window = await database.MaintenanceWindows.AsNoTracking().SingleAsync(item => item.Id == windowId);
        window.ExpandedThrough.Should().Be(clock.GetUtcNow().AddDays(horizonDays));

        await VerifyDuplicateOccurrenceStartRejectedAsync(connectionString, windowId, extendedStarts[0]);

        (await maintenanceService.CancelAsync(new(windowId, window.Version), access))
            .Succeeded.Should().BeTrue();
        database.ChangeTracker.Clear();
        (await maintenanceEvaluator.FindActiveAsync(monitor.Id, initialStarts[1])).Should().BeNull(
            "a cancelled recurrence suppresses nothing, even where its occurrences remain for history");
    }

    private static async Task<IReadOnlyList<DateTimeOffset>> OccurrenceStartsAsync(
        ApplicationDbContext database, Guid windowId) =>
        await database.MaintenanceOccurrences.AsNoTracking()
            .Where(occurrence => occurrence.MaintenanceWindowId == windowId)
            .OrderBy(occurrence => occurrence.StartsAt)
            .Select(occurrence => occurrence.StartsAt)
            .ToArrayAsync();

    private static async Task VerifyDuplicateOccurrenceStartRejectedAsync(
        string connectionString, Guid windowId, DateTimeOffset startsAt)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO web_health.maintenance_occurrence (id, maintenance_window_id, starts_at, ends_at, created_at)
            VALUES (@id, @window_id, @starts_at, @starts_at + interval '2 hours', now());
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("window_id", windowId);
        command.Parameters.AddWithValue("starts_at", startsAt);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        exception.ConstraintName.Should().Be("ux_maintenance_occurrence_window_start");
    }

    /// <summary>
    /// Phase 6 increment 6.7. The crawl schema owns its own endpoint fixture rather than reusing
    /// one an earlier stage created, so nothing it asserts depends on what ran before it.
    /// </summary>

    /// <summary>
    /// Archiving an endpoint hides it; purging one removes it. This stage seeds a row in every
    /// table that can reference an endpoint, purges it, and asserts each of them is empty
    /// afterwards. Every foreign key here is RESTRICT, so a table the cascade forgot does not
    /// leave a dangling reference - it aborts the purge with 23503, which is what makes an
    /// inventory-shaped assertion the right one: the stage fails whether the cascade misses a
    /// table or unwinds them in the wrong order.
    /// </summary>
    private static async Task VerifyEndpointPurgeRemovesEveryReferenceAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging()
            .AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var endpointService = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var administratorAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var operationsAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Operations]);
        var ownerSubjectId = await database.OwnerSubjects
            .Where(owner => owner.UserId == administrator.Id).Select(owner => owner.Id).SingleAsync();

        // HTTPS so the endpoint owns both monitor kinds: the certificate monitor carries the
        // certificate observation and the still-held lease seeded below.
        var monitor = await CreateOwnedMonitorAsync(
            scope, database, "https://endpoint-purge.test/status");
        var endpointId = monitor.EndpointId;
        var certificateMonitor = await database.EndpointMonitors.SingleAsync(candidate =>
            candidate.EndpointId == endpointId
            && candidate.MonitorType == RegistryDefaults.SslCertificateMonitorType
            && candidate.DeletedAt == null);

        var now = DateTimeOffset.UtcNow;
        var checkId = await FinalizeScheduledResultAsync(
            database, monitor, 500,
            htmlBody: "<html><head><title>Purge fixture</title></head><body>body</body></html>");
        database.ChangeTracker.Clear();

        database.RedirectHops.Add(new RedirectHop
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = checkId,
            HopNumber = 1,
            NormalizedFromUrl = "https://endpoint-purge.test/status",
            NormalizedToUrl = "https://endpoint-purge.test/status/",
            HttpStatus = 301,
            IsLoop = false
        });

        // A lease is what an interrupted worker leaves behind, so the purge has to survive one
        // rather than assume every check reached a clean finalization.
        var certificateCheckId = Guid.NewGuid();
        var certificateCheckCreatedAt = now.AddMinutes(-3);
        var certificateCheck = new LogicalCheck
        {
            Id = certificateCheckId,
            EndpointMonitorId = certificateMonitor.Id,
            Source = LogicalCheckSources.Scheduled,
            ScheduledFor = certificateCheckCreatedAt,
            State = LogicalCheckStates.Running,
            CadenceKey = MonitorCadence.CreateCadenceKey(certificateMonitor.Id, certificateCheckCreatedAt),
            PolicyFingerprint = certificateMonitor.ConfigurationFingerprint,
            CreatedAt = certificateCheckCreatedAt,
            QueuedAt = certificateCheckCreatedAt,
            StartedAt = certificateCheckCreatedAt
        };
        certificateCheck.ConfigurationSnapshot = new CheckConfigurationSnapshot
        {
            LogicalCheckId = certificateCheckId,
            SchemaVersion = 2,
            MonitorType = certificateMonitor.MonitorType,
            ConfigurationFingerprint = certificateMonitor.ConfigurationFingerprint,
            IntervalSeconds = certificateMonitor.IntervalSeconds,
            TimeoutSeconds = certificateMonitor.TimeoutSeconds,
            FailureConfirmationCount = certificateMonitor.FailureConfirmationCount,
            RecoveryConfirmationCount = certificateMonitor.RecoveryConfirmationCount,
            IntervalSource = ConfigurationValueSources.EnvironmentDefault,
            TimeoutSource = ConfigurationValueSources.PolicyProfile,
            ConfirmationSource = ConfigurationValueSources.PolicyProfile,
            ThresholdSource = ConfigurationValueSources.PolicyProfile,
            CreatedAt = certificateCheckCreatedAt
        };
        database.LogicalChecks.Add(certificateCheck);
        database.ExecutionLeases.Add(new ExecutionLease
        {
            EndpointMonitorId = certificateMonitor.Id,
            LogicalCheckId = certificateCheckId,
            OwnerToken = Guid.NewGuid(),
            FencingGeneration = 1,
            AcquiredAt = certificateCheckCreatedAt,
            ExpiresAt = certificateCheckCreatedAt.AddMinutes(5)
        });
        database.EndpointHealth.Add(new EndpointHealth
        {
            EndpointMonitorId = certificateMonitor.Id,
            EvidenceLogicalCheckId = certificateCheckId,
            ConfirmedStatus = "Critical",
            ConfirmedAt = now,
            Version = 1
        });
        database.CertificateObservations.Add(new CertificateObservation
        {
            LogicalCheckId = certificateCheckId,
            EndpointMonitorId = certificateMonitor.Id,
            Subject = "CN=endpoint-purge.test",
            Issuer = "CN=Purge Fixture CA",
            SerialNumber = "01",
            Sha256Fingerprint = new string('a', 64),
            NotBefore = now.AddDays(-10),
            NotAfter = now.AddDays(30),
            DaysRemaining = 30,
            ValidationCategory = TlsValidationCategory.Valid.ToString(),
            HostnameMatched = true,
            ChainTrusted = true,
            ObservedAt = now
        });
        await database.SaveChangesAsync();

        var (incident, openedEvent) = await CreateOpenIncidentAsync(
            database, monitor.Id, ownerSubjectId, "purge-fixture:unreachable", now);
        database.IncidentEvidence.Add(new IncidentEvidence
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            EndpointMonitorId = monitor.Id,
            LogicalCheckId = checkId,
            EvidenceType = IncidentEvidenceTypes.Opening,
            EvidenceRole = "CheckResult",
            BoundedSnapshot = "{}",
            CapturedAt = now
        });

        // A recurrence chain, so the self-reference the cascade has to detach is really present.
        var previousIncident = new Incident
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = monitor.Id,
            OwnerSubjectId = ownerSubjectId,
            IssueKey = "purge-fixture:recurred",
            Severity = IncidentSeverities.Critical,
            Status = IncidentStatuses.Closed,
            OpenedAt = now.AddHours(-2),
            ResolvedAt = now.AddHours(-1),
            ClosedAt = now.AddHours(-1),
            ResolutionCategory = "AutomaticRecovery",
            ResolutionNote = "Recovered before the purge fixture archived the endpoint.",
            Version = 1
        };
        database.Incidents.Add(previousIncident);
        await database.SaveChangesAsync();

        var chained = await database.Incidents.SingleAsync(candidate => candidate.Id == incident.Id);
        chained.PreviousIncidentId = previousIncident.Id;
        chained.RecurrenceCount = 1;

        var notificationEventId = Guid.NewGuid();
        database.NotificationEvents.Add(new NotificationEvent
        {
            Id = notificationEventId,
            IncidentId = incident.Id,
            IncidentEventId = openedEvent.Id,
            SourceKind = NotificationSourceKinds.IncidentEvent,
            EventType = NotificationEventTypes.Opened,
            OccurrenceKey = $"purge-fixture|{incident.Id:N}|opened",
            TemplateVersion = "v1",
            IsSuppressed = false,
            OccurredAt = now
        });
        var deliveryId = Guid.NewGuid();
        database.NotificationDeliveries.Add(new NotificationDelivery
        {
            Id = deliveryId,
            NotificationEventId = notificationEventId,
            Channel = NotificationChannels.Email,
            NormalizedRecipient = "purge-fixture@example.test",
            RecipientNormalizationVersion = RecipientNormalizer.Version,
            State = NotificationDeliveryStates.Sent,
            AttemptCount = 1,
            SentAt = now
        });
        database.NotificationAttempts.Add(new NotificationAttempt
        {
            Id = Guid.NewGuid(),
            NotificationDeliveryId = deliveryId,
            AttemptNumber = 1,
            TransportOutcome = NotificationTransportOutcomes.Sent,
            AttemptedAt = now
        });

        var runId = Guid.NewGuid();
        database.CrawlRuns.Add(new CrawlRun
        {
            Id = runId,
            EndpointId = endpointId,
            Status = CrawlRunStatuses.Completed,
            StopReason = CrawlStopReasons.FrontierExhausted,
            SeedUrls = "https://endpoint-purge.test/",
            QueryPolicy = "Canonicalize",
            MaxPages = 10,
            MaxDepth = 2,
            CheckExternalLinks = false,
            PagesFetched = 1,
            LinksRecorded = 1,
            RobotsOverrideGranted = false,
            RobotsOverrideRefusedBecause = "NotRequested",
            StartedAt = now.AddMinutes(-5),
            FinishedAt = now
        });
        database.CrawlLinkResults.Add(new CrawlLinkResult
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            SourceUrl = "https://endpoint-purge.test/",
            SourceUrlHash = new byte[32],
            TargetUrl = "https://endpoint-purge.test/missing",
            TargetUrlHash = Enumerable.Repeat((byte)1, 32).ToArray(),
            Classification = CrawlLinkClassifications.Broken,
            StatusCode = 404,
            RedirectCount = 0,
            IsInternal = true,
            Depth = 1,
            DurationMs = 12,
            RecordedAt = now
        });

        var windowId = Guid.NewGuid();
        database.MaintenanceWindows.Add(new MaintenanceWindow
        {
            Id = windowId,
            CreatedByUserId = administrator.Id,
            Reason = "Purge fixture window",
            TimezoneId = "UTC",
            SuppressionPolicy = MaintenanceSuppressionPolicies.SuppressAll,
            ScheduleStartsAt = now.AddHours(1),
            ScheduleDurationSeconds = 3600,
            RecurrencePattern = MaintenanceRecurrencePatterns.None,
            PauseEscalation = true,
            ContinueFailureCounter = false,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = administrator.Id
        });
        database.MaintenanceTargets.Add(new MaintenanceTarget
        {
            Id = Guid.NewGuid(),
            MaintenanceWindowId = windowId,
            EndpointId = endpointId
        });
        database.MaintenanceOccurrences.Add(new MaintenanceOccurrence
        {
            Id = Guid.NewGuid(),
            MaintenanceWindowId = windowId,
            StartsAt = now.AddHours(1),
            EndsAt = now.AddHours(2),
            CreatedAt = now
        });

        database.AccessGrants.Add(new AccessGrant
        {
            Id = Guid.NewGuid(),
            UserId = administrator.Id,
            AccessLevel = "Read",
            EndpointId = endpointId,
            EffectiveFrom = now,
            CreatedAt = now,
            CreatedByUserId = administrator.Id
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        // BR-E06 keeps one robots policy per origin, shared by every endpoint on the host, so a
        // purge may only take it once it has taken the last of them. A second endpoint on the
        // same origin is what makes both halves of that rule observable.
        var neighbour = await CreateOwnedMonitorAsync(
            scope, database, "https://endpoint-purge.test/second");
        database.RobotsSnapshots.Add(new RobotsSnapshot
        {
            Origin = "https://endpoint-purge.test",
            Host = "endpoint-purge.test",
            Port = 443,
            Status = "Fetched",
            Content = "User-agent: *\nAllow: /",
            SitemapRequired = false,
            SitemapAvailable = false,
            Version = 1,
            FetchedAt = now,
            ExpiresAt = now.AddHours(6),
            UpdatedAt = now
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var seeded = await CountEndpointReferencesAsync(database, endpointId, windowId);
        seeded.Should().OnlyContain(entry => entry.Value > 0,
            "the purge assertion is only evidence if every table it clears held a row first");

        // The purge is the only caller exempt from the evidence-immutability triggers, and the
        // exemption is what makes an ordinary delete against these tables still impossible.
        await VerifyEvidenceDeleteRejectedAsync(
            connectionString, "incident_event", openedEvent.Id, "incident_event rows are immutable");
        await VerifyEvidenceDeleteRejectedAsync(
            connectionString, "check_configuration_snapshot", checkId,
            "check_configuration_snapshot rows are immutable", "logical_check_id");

        // The archive step is a precondition, not a formality: purging a live endpoint is refused.
        var live = await database.Endpoints.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == endpointId);
        (await endpointService.PurgeAsync(new(endpointId, live.Version), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);

        var archived = await endpointService.DeleteAsync(new(endpointId, live.Version), administratorAccess);
        archived.Succeeded.Should().BeTrue(string.Join(" ", archived.Errors));
        database.ChangeTracker.Clear();

        var archivedVersion = await database.Endpoints.AsNoTracking()
            .Where(candidate => candidate.Id == endpointId)
            .Select(candidate => candidate.Version).SingleAsync();

        // Managing the registry is not enough. A purge is irreversible, so it is Administrator-only.
        (await endpointService.PurgeAsync(new(endpointId, archivedVersion), operationsAccess))
            .Status.Should().Be(RegistryMutationStatus.Forbidden);
        (await endpointService.PurgeAsync(new(endpointId, archivedVersion - 1), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ConcurrencyConflict);

        var purged = await endpointService.PurgeAsync(new(endpointId, archivedVersion), administratorAccess);
        purged.Succeeded.Should().BeTrue(string.Join(" ", purged.Errors));
        database.ChangeTracker.Clear();

        var remaining = await CountEndpointReferencesAsync(database, endpointId, windowId);
        remaining.Should().OnlyContain(entry => entry.Value == 0,
            "a purged endpoint leaves nothing behind for the dashboard, SEO, incident, crawl or "
            + "notification surfaces to read");

        // The audit trail is keyed by identifier rather than by foreign key, so it outlives the
        // endpoint and stays the only remaining evidence that it ever existed.
        (await database.AuditEvents.AsNoTracking().CountAsync(item =>
            item.EntityType == "endpoint"
            && item.EntityIdentifier == endpointId.ToString()
            && item.Action == "endpoint.purged"))
            .Should().Be(1);

        // The neighbour still holds the origin, so its robots policy is not the purged endpoint's
        // to take with it.
        (await database.RobotsSnapshots.AsNoTracking()
            .CountAsync(snapshot => snapshot.Host == "endpoint-purge.test"))
            .Should().Be(1, "an origin shared with a surviving endpoint keeps its robots policy");

        var neighbourLive = await database.Endpoints.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == neighbour.EndpointId);
        (await endpointService.DeleteAsync(
            new(neighbour.EndpointId, neighbourLive.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        database.ChangeTracker.Clear();
        var neighbourVersion = await database.Endpoints.AsNoTracking()
            .Where(candidate => candidate.Id == neighbour.EndpointId)
            .Select(candidate => candidate.Version).SingleAsync();
        var neighbourPurged = await endpointService.PurgeAsync(
            new(neighbour.EndpointId, neighbourVersion), administratorAccess);
        neighbourPurged.Succeeded.Should().BeTrue(string.Join(" ", neighbourPurged.Errors));
        database.ChangeTracker.Clear();

        // Nothing sits on the origin now. Leaving the policy would hand a future endpoint on this
        // host a cached decision - an approved robots exception included - that nobody granted it.
        (await database.RobotsSnapshots.AsNoTracking()
            .CountAsync(snapshot => snapshot.Host == "endpoint-purge.test"))
            .Should().Be(0, "the last endpoint on an origin takes its robots policy with it");

        // Other origins are untouched: the rule is scoped to the host that was emptied.
        (await database.RobotsSnapshots.AsNoTracking().CountAsync()).Should().BeGreaterThan(0);
    }

    /// <summary>
    /// A delete against an evidence table outside an endpoint purge is still rejected. The purge
    /// opens its exemption with <c>SET LOCAL</c>, so a connection that never set it - which is
    /// every other caller - sees the trigger unchanged.
    /// </summary>
    private static async Task VerifyEvidenceDeleteRejectedAsync(
        string connectionString,
        string table,
        Guid id,
        string expectedMessage,
        string keyColumn = "id")
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DELETE FROM web_health.{table} WHERE {keyColumn} = @id", connection);
        command.Parameters.AddWithValue("id", id);
        var rejection = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync());
        rejection.SqlState.Should().Be(PostgresErrorCodes.RaiseException);
        rejection.MessageText.Should().Be(expectedMessage);
    }

    /// <summary>
    /// Every table that can reach an endpoint, counted by name. Returned as a map rather than
    /// asserted one call at a time so a missed table reads as a named non-zero entry instead of
    /// as a bare false.
    /// </summary>
    private static async Task<Dictionary<string, int>> CountEndpointReferencesAsync(
        ApplicationDbContext database,
        Guid endpointId,
        Guid maintenanceWindowId)
    {
        var monitors = database.EndpointMonitors
            .Where(monitor => monitor.EndpointId == endpointId).Select(monitor => monitor.Id);
        var checks = database.LogicalChecks
            .Where(check => monitors.Contains(check.EndpointMonitorId)).Select(check => check.Id);
        var incidents = database.Incidents
            .Where(incident => monitors.Contains(incident.EndpointMonitorId)).Select(incident => incident.Id);
        var notifications = database.NotificationEvents
            .Where(notification => incidents.Contains(notification.IncidentId)).Select(notification => notification.Id);
        var deliveries = database.NotificationDeliveries
            .Where(delivery => notifications.Contains(delivery.NotificationEventId)).Select(delivery => delivery.Id);
        var runs = database.CrawlRuns
            .Where(run => run.EndpointId == endpointId).Select(run => run.Id);

        return new Dictionary<string, int>
        {
            ["endpoint"] = await database.Endpoints.CountAsync(item => item.Id == endpointId),
            ["endpoint_monitor"] = await database.EndpointMonitors.CountAsync(item => item.EndpointId == endpointId),
            ["target_authorization"] = await database.TargetAuthorizations.CountAsync(item => item.EndpointId == endpointId),
            ["access_grant"] = await database.AccessGrants.CountAsync(item => item.EndpointId == endpointId),
            ["logical_check"] = await database.LogicalChecks.CountAsync(item => monitors.Contains(item.EndpointMonitorId)),
            ["check_configuration_snapshot"] = await database.CheckConfigurationSnapshots.CountAsync(item => checks.Contains(item.LogicalCheckId)),
            ["execution_attempt"] = await database.ExecutionAttempts.CountAsync(item => checks.Contains(item.LogicalCheckId)),
            ["durable_work"] = await database.DurableWork.CountAsync(item => checks.Contains(item.LogicalCheckId)),
            ["execution_lease"] = await database.ExecutionLeases.CountAsync(item => monitors.Contains(item.EndpointMonitorId)),
            ["check_result"] = await database.CheckResults.CountAsync(item => checks.Contains(item.LogicalCheckId)),
            ["redirect_hop"] = await database.RedirectHops.CountAsync(item => checks.Contains(item.LogicalCheckId)),
            ["finding"] = await database.Findings.CountAsync(item => checks.Contains(item.LogicalCheckId)),
            ["seo_observation"] = await database.SeoObservations.CountAsync(item => checks.Contains(item.LogicalCheckId)),
            ["certificate_observation"] = await database.CertificateObservations.CountAsync(item => checks.Contains(item.LogicalCheckId)),
            ["endpoint_health"] = await database.EndpointHealth.CountAsync(item => monitors.Contains(item.EndpointMonitorId)),
            ["issue_state"] = await database.IssueStates.CountAsync(item => monitors.Contains(item.EndpointMonitorId)),
            ["incident"] = await database.Incidents.CountAsync(item => monitors.Contains(item.EndpointMonitorId)),
            ["incident_event"] = await database.IncidentEvents.CountAsync(item => incidents.Contains(item.IncidentId)),
            ["incident_evidence"] = await database.IncidentEvidence.CountAsync(item => incidents.Contains(item.IncidentId)),
            ["notification_event"] = await database.NotificationEvents.CountAsync(item => incidents.Contains(item.IncidentId)),
            ["notification_delivery"] = await database.NotificationDeliveries.CountAsync(item => notifications.Contains(item.NotificationEventId)),
            ["notification_attempt"] = await database.NotificationAttempts.CountAsync(item => deliveries.Contains(item.NotificationDeliveryId)),
            ["crawl_run"] = await database.CrawlRuns.CountAsync(item => item.EndpointId == endpointId),
            ["crawl_link_result"] = await database.CrawlLinkResults.CountAsync(item => runs.Contains(item.RunId)),
            ["maintenance_window"] = await database.MaintenanceWindows.CountAsync(item => item.Id == maintenanceWindowId),
            ["maintenance_target"] = await database.MaintenanceTargets.CountAsync(item => item.MaintenanceWindowId == maintenanceWindowId),
            ["maintenance_occurrence"] = await database.MaintenanceOccurrences.CountAsync(item => item.MaintenanceWindowId == maintenanceWindowId)
        };
    }

    private static async Task VerifyCrawlResultContractAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging()
            .AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var schemaMonitor = await CreateOwnedMonitorAsync(
            scope, database, "http://crawl-schema.test/status");
        await CrawlSchemaAssertions.VerifyAsync(connectionString, schemaMonitor.EndpointId);

        // A separate endpoint for the comparison: it asserts an endpoint's whole run history, and
        // sharing the schema fixture would mix the rejected and plan-evidence runs into it.
        var comparisonMonitor = await CreateOwnedMonitorAsync(
            scope, database, "http://crawl-comparison.test/status");
        await CrawlSchemaAssertions.VerifyComparisonAsync(
            connectionString, comparisonMonitor.EndpointId);
    }

    /// <summary>
    /// BR-E01 and BR-E10. The applicability contract is enforced by the database, not only by the
    /// extractor: a NotApplicable row records why and carries no extracted values, an Applicable
    /// row carries no reason, and a stored value can never claim to be longer than it is. There is
    /// no column that could hold the document at all, which is asserted here by name.
    /// </summary>
    private static async Task VerifySeoObservationContractAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var columns = new List<string>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'web_health' AND table_name = 'seo_observation'
            ORDER BY column_name;
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        }

        columns.Should().BeEquivalentTo(
            "logical_check_id", "endpoint_monitor_id", "applicability", "not_applicable_reason",
            "document_truncated", "title", "title_length", "title_count", "meta_description",
            "meta_description_length", "meta_description_count", "canonical_href", "canonical_length",
            "canonical_count", "canonical_absolute_url", "robots_meta", "robots_meta_length",
            "robots_meta_count", "observed_at",
            "policy_description_required", "policy_expected_host", "policy_indexing_expectation");

        await VerifySeoObservationRejectedAsync(
            connectionString,
            "'NotApplicable', 'NonHtml', 'A title'",
            "ck_seo_observation_applicability_fields");
        await VerifySeoObservationRejectedAsync(
            connectionString,
            "'Applicable', 'NonHtml', NULL",
            "ck_seo_observation_applicability_fields");
        await VerifySeoObservationRejectedAsync(
            connectionString,
            "'Sometimes', NULL, NULL",
            "ck_seo_observation_applicability");
        await VerifySeoDocumentIsNeverRetainedAsync(connectionString);
    }

    /// <summary>
    /// BR-E10 asserted as absence at the boundary that matters: after a real check finalises a
    /// document whose body, comments, scripts and unrelated metadata all carry a distinctive
    /// marker, no stored column of the SEO observation, the check result, its findings, or the
    /// audit trail may contain it. The extracted values are still there, which is the point —
    /// values are kept, the document is not.
    /// </summary>
    private static async Task VerifySeoDocumentIsNeverRetainedAsync(string connectionString)
    {
        const string marker = "SECRET-DOCUMENT-MARKER-4b91e7";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var monitor = await AvailabilityMonitors(database)
            .Include(candidate => candidate.Endpoint).ThenInclude(endpoint => endpoint.Environment)
            .Where(candidate => candidate.DeletedAt == null)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstAsync();

        var checkId = await FinalizeScheduledResultAsync(database, monitor, 200, null, $"""
            <!doctype html><html><head>
            <title>Extracted title</title>
            <meta name="description" content="Extracted description.">
            <link rel="canonical" href="https://example.test/canonical">
            <meta name="robots" content="index, follow">
            <meta name="author" content="{marker}">
            <!-- {marker} -->
            <script>var leaked = "{marker}";</script>
            </head><body><h1>{marker}</h1><p>{marker}</p></body></html>
            """);

        database.ChangeTracker.Clear();
        var observation = await database.SeoObservations.AsNoTracking()
            .SingleAsync(candidate => candidate.LogicalCheckId == checkId);
        observation.Applicability.Should().Be(SeoApplicabilities.Applicable);
        observation.Title.Should().Be("Extracted title");
        observation.MetaDescription.Should().Be("Extracted description.");
        observation.CanonicalAbsoluteUrl.Should().Be("https://example.test/canonical");
        observation.RobotsMeta.Should().Be("index, follow");

        // Every text column of every table that could plausibly carry it, including columns added
        // later: the absence claim must not quietly stop covering a new column.
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        // The marker is interpolated rather than bound: a DO block is an opaque string to the
        // server, so a parameter placeholder inside it is never substituted and reads back as an
        // operator applied to a missing column.
        await using var command = new NpgsqlCommand(
            $"""
            DO $$
            DECLARE
                target record;
                hits bigint;
            BEGIN
                FOR target IN
                    SELECT table_name, column_name FROM information_schema.columns
                    WHERE table_schema = 'web_health'
                      AND table_name IN ('seo_observation', 'check_result', 'finding', 'audit_event')
                      AND data_type IN ('text', 'character varying', 'jsonb', 'json')
                LOOP
                    EXECUTE format(
                        'SELECT count(*) FROM web_health.%I WHERE %I::text LIKE $1',
                        target.table_name, target.column_name)
                    INTO hits USING '%{marker}%';

                    IF hits > 0 THEN
                        RAISE EXCEPTION
                            'BR-E10 violated: web_health.%.% retained document content.',
                            target.table_name, target.column_name;
                    END IF;
                END LOOP;
            END $$;
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task VerifySeoObservationRejectedAsync(
        string connectionString, string values, string expectedConstraint)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO web_health.seo_observation
                (logical_check_id, endpoint_monitor_id, applicability, not_applicable_reason, title,
                 title_length, title_count, meta_description_length, meta_description_count,
                 canonical_length, canonical_count, robots_meta_length, robots_meta_count, observed_at,
                 document_truncated)
            SELECT check_row.id, check_row.endpoint_monitor_id, {values}, 0, 0, 0, 0, 0, 0, 0, 0, now(), false
            FROM web_health.logical_check AS check_row
            WHERE NOT EXISTS (
                SELECT 1 FROM web_health.seo_observation AS existing
                WHERE existing.logical_check_id = check_row.id)
            LIMIT 1;
            """, connection);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.Should().Be(expectedConstraint);
    }

    private static readonly MaintenanceRecurrenceSpec OneOff =
        new(MaintenanceRecurrencePatterns.None, MaintenanceDayOfWeekMask.Empty, null);

    private static async Task VerifyMaintenanceClassifiedResultRetentionAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var maintenanceService = scope.ServiceProvider.GetRequiredService<IMaintenanceWindowService>();

        var monitor = await CreateOwnedMonitorAsync(scope, database, "http://maintenance-retention.test/status");

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var administratorAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var maintenanceWindow = await maintenanceService.CreateAsync(new(
            new(MaintenanceScopeKind.Monitor, monitor.Id),
            clock.GetUtcNow().AddMinutes(-5),
            clock.GetUtcNow().AddHours(1),
            "UTC",
            "Retention verification",
            MaintenanceSuppressionPolicies.SuppressAll,
            true,
            false,
            OneOff), administratorAccess);
        maintenanceWindow.Succeeded.Should().BeTrue(string.Join(" ", maintenanceWindow.Errors));

        var checkId = await FinalizeScheduledResultAsync(database, monitor, 500, clock);

        database.ChangeTracker.Clear();
        var result = await database.CheckResults.AsNoTracking().SingleAsync(candidate => candidate.LogicalCheckId == checkId);
        result.IsMaintenance.Should().BeTrue();
        result.MaintenanceOccurrenceId.Should().NotBeNull();
        result.CountsForUptime.Should().BeFalse();

        // BR-M04 default: the failure-confirmation counter resets during maintenance, so this
        // single failure neither creates an issue_state row nor opens an incident.
        (await database.IssueStates.AnyAsync(state => state.EndpointMonitorId == monitor.Id)).Should().BeFalse();
        (await database.Incidents.AnyAsync(incident => incident.EndpointMonitorId == monitor.Id)).Should().BeFalse();

        (await maintenanceService.CancelAsync(new(maintenanceWindow.MaintenanceWindowId!.Value, 1), administratorAccess))
            .Succeeded.Should().BeTrue();
    }

    private static async Task<(Incident Incident, IncidentEvent OpenedEvent)> CreateOpenIncidentAsync(
        ApplicationDbContext database,
        Guid monitorId,
        Guid ownerSubjectId,
        string issueKey,
        DateTimeOffset now)
    {
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = monitorId,
            OwnerSubjectId = ownerSubjectId,
            IssueKey = issueKey,
            Severity = IncidentSeverities.Critical,
            Status = IncidentStatuses.Open,
            OpenedAt = now,
            Version = 1
        };
        database.Incidents.Add(incident);
        var openedEvent = new IncidentEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            SequenceNumber = 1,
            EventType = IncidentEventTypes.Opened,
            ToStatus = IncidentStatuses.Open,
            OccurredAt = now
        };
        database.IncidentEvents.Add(openedEvent);
        await database.SaveChangesAsync();
        return (incident, openedEvent);
    }

    private static async Task VerifyDuplicateNotificationEventRejectedAsync(
        string connectionString, Guid incidentId, Guid incidentEventId, string occurrenceKey)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Same (incident_id, source_kind, event_type, occurrence_key) as the row already written
        // for this incident, and otherwise a fully valid row (real incident_event_id) — isolates
        // the uniqueness index from the field-group check constraint exercised below.
        const string duplicateSql = """
            INSERT INTO web_health.notification_event
                (id, incident_event_id, incident_id, source_kind, event_type, occurrence_key,
                 template_version, is_suppressed, occurred_at)
            VALUES (@id, @incident_event_id, @incident_id, 'IncidentEvent', 'Opened', @occurrence_key, 'v1', FALSE, now());
            """;
        await using (var duplicateCommand = new NpgsqlCommand(duplicateSql, connection))
        {
            duplicateCommand.Parameters.AddWithValue("id", Guid.NewGuid());
            duplicateCommand.Parameters.AddWithValue("incident_event_id", incidentEventId);
            duplicateCommand.Parameters.AddWithValue("incident_id", incidentId);
            duplicateCommand.Parameters.AddWithValue("occurrence_key", occurrenceKey);
            var duplicateException = await Assert.ThrowsAsync<PostgresException>(
                () => duplicateCommand.ExecuteNonQueryAsync());
            duplicateException.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
            duplicateException.ConstraintName.Should().Be("ux_notification_event_occurrence");
        }

        // source_kind = 'IncidentEvent' requires a non-null incident_event_id; a null one must be
        // rejected by the field-group check constraint, independent of the uniqueness index above.
        const string missingLinkSql = """
            INSERT INTO web_health.notification_event
                (id, incident_event_id, incident_id, source_kind, event_type, occurrence_key,
                 template_version, is_suppressed, occurred_at)
            VALUES (@id, NULL, @incident_id, 'IncidentEvent', 'Opened', @new_occurrence_key, 'v1', FALSE, now());
            """;
        await using var missingLinkCommand = new NpgsqlCommand(missingLinkSql, connection);
        missingLinkCommand.Parameters.AddWithValue("id", Guid.NewGuid());
        missingLinkCommand.Parameters.AddWithValue("incident_id", incidentId);
        missingLinkCommand.Parameters.AddWithValue("new_occurrence_key", $"{occurrenceKey}|missing-link");
        var missingLinkException = await Assert.ThrowsAsync<PostgresException>(
            () => missingLinkCommand.ExecuteNonQueryAsync());
        missingLinkException.ConstraintName.Should().Be("ck_notification_event_incident_event_required");
    }

    private static async Task VerifyTransientFailureRetriesThenFailsPermanentlyAsync(
        string connectionString,
        ApplicationDbContext database,
        Guid monitorId,
        Guid ownerSubjectId)
    {
        var now = DateTimeOffset.UtcNow;
        var (incident, openedEvent) = await CreateOpenIncidentAsync(
            database, monitorId, ownerSubjectId, $"v1|HttpAvailability|notification-retry|{Guid.NewGuid():N}", now);
        var writer = new NotificationEventWriter(database);
        await writer.WriteAsync(
            incident, openedEvent.Id, NotificationSourceKinds.IncidentEvent, NotificationEventTypes.Opened,
            NotificationOccurrenceKeys.Opening(incident.Id), isMaintenance: false, now, default);
        await database.SaveChangesAsync();
        var deliveryId = await database.NotificationDeliveries.AsNoTracking()
            .Where(candidate => candidate.NotificationEvent.IncidentId == incident.Id)
            .Select(candidate => candidate.Id)
            .SingleAsync();

        var failingConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        var failingTransport = new AlwaysTransientFailureEmailTransport();
        await using var failingServices = new ServiceCollection().AddLogging()
            .AddInfrastructure(failingConfiguration)
            .Replace(ServiceDescriptor.Singleton<IEmailTransport>(failingTransport))
            .BuildServiceProvider();
        await using var failingScope = failingServices.CreateAsyncScope();
        var failingDispatcher = failingScope.ServiceProvider.GetRequiredService<NotificationDispatchService>();
        var failingDatabase = failingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await failingDispatcher.DispatchDueAsync();
        var afterFirstFailure = await failingDatabase.NotificationDeliveries.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == deliveryId);
        afterFirstFailure.State.Should().Be(NotificationDeliveryStates.RetryScheduled);
        afterFirstFailure.NextAttemptAt.Should().NotBeNull();
        afterFirstFailure.AttemptCount.Should().Be(1);

        var maxAttempts = new NotificationSchedulingOptions().MaxAttempts;
        for (var attempt = afterFirstFailure.AttemptCount; attempt < maxAttempts; attempt++)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "UPDATE web_health.notification_delivery SET next_attempt_at = now() - interval '1 minute' WHERE id = @id",
                connection);
            command.Parameters.AddWithValue("id", deliveryId);
            await command.ExecuteNonQueryAsync();
            await failingDispatcher.DispatchDueAsync();
        }

        var finalState = await failingDatabase.NotificationDeliveries.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == deliveryId);
        finalState.State.Should().Be(NotificationDeliveryStates.FailedPermanently);
        finalState.NextAttemptAt.Should().BeNull();
        (await failingDatabase.NotificationAttempts.AsNoTracking()
            .CountAsync(attempt => attempt.NotificationDeliveryId == deliveryId)).Should().Be(maxAttempts);

        // Item 9: dispatch runs in its own transaction, entirely outside the one that opened the
        // incident. A permanently-failed SMTP delivery must leave the incident row completely
        // untouched — same Version, same Status, no rollback of already-committed state.
        var incidentAfterPermanentFailure = await failingDatabase.Incidents.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == incident.Id);
        incidentAfterPermanentFailure.Version.Should().Be(incident.Version);
        incidentAfterPermanentFailure.Status.Should().Be(incident.Status);

        await AcknowledgeAndResolveAsync(failingDatabase, incident.Id);
    }

    private sealed class AlwaysTransientFailureEmailTransport : IEmailTransport
    {
        public Task<EmailTransportResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmailTransportResult(EmailTransportOutcome.TransientFailure, "simulated outage"));
    }

    private static async Task VerifyDuplicateIssueStateRejectedAsync(
        string connectionString, Guid monitorId, string issueKey)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO web_health.issue_state
                (id, endpoint_monitor_id, issue_key, consecutive_failures, consecutive_recoveries, updated_at, version)
            VALUES (@id, @monitor_id, @issue_key, 0, 0, now(), 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("monitor_id", monitorId);
        command.Parameters.AddWithValue("issue_key", issueKey);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        exception.ConstraintName.Should().Be("ix_issue_state_endpoint_monitor_id_issue_key");
    }

    private static async Task VerifyDuplicateActiveIncidentRejectedAsync(
        string connectionString, Guid monitorId, Guid ownerSubjectId, string issueKey)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO web_health.incident
                (id, endpoint_monitor_id, owner_subject_id, issue_key, severity, status,
                 recurrence_count, opened_at, version)
            VALUES (@id, @monitor_id, @owner_subject_id, @issue_key, 'Critical', 'Open', 0, now(), 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("monitor_id", monitorId);
        command.Parameters.AddWithValue("owner_subject_id", ownerSubjectId);
        command.Parameters.AddWithValue("issue_key", issueKey);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        exception.ConstraintName.Should().Be("ix_incident_endpoint_monitor_id_issue_key");
    }

    private static async Task VerifyIncidentResolutionFieldsRejectedAsync(
        string connectionString, Guid monitorId, Guid ownerSubjectId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO web_health.incident
                (id, endpoint_monitor_id, owner_subject_id, issue_key, severity, status,
                 recurrence_count, opened_at, acknowledged_at, version)
            VALUES (@id, @monitor_id, @owner_subject_id, @issue_key, 'Warning', 'Resolved', 0, now(), now(), 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("monitor_id", monitorId);
        command.Parameters.AddWithValue("owner_subject_id", ownerSubjectId);
        command.Parameters.AddWithValue("issue_key", $"v1|HttpAvailability|incomplete-resolution|{Guid.NewGuid():N}");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("ck_incident_resolution_complete");
    }

    private static async Task VerifyIncidentAcknowledgedFieldExactnessRejectedAsync(
        string connectionString, Guid monitorId, Guid ownerSubjectId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO web_health.incident
                (id, endpoint_monitor_id, owner_subject_id, issue_key, severity, status,
                 recurrence_count, opened_at, acknowledged_at, version)
            VALUES (@id, @monitor_id, @owner_subject_id, @issue_key, 'Critical', 'Open', 0, now(), now(), 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("monitor_id", monitorId);
        command.Parameters.AddWithValue("owner_subject_id", ownerSubjectId);
        command.Parameters.AddWithValue("issue_key", $"v1|HttpAvailability|open-with-ack|{Guid.NewGuid():N}");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("ck_incident_acknowledged_fields");
    }

    private static async Task VerifyIncidentClosedFieldExactnessRejectedAsync(
        string connectionString, Guid monitorId, Guid ownerSubjectId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO web_health.incident
                (id, endpoint_monitor_id, owner_subject_id, issue_key, severity, status,
                 recurrence_count, opened_at, acknowledged_at, resolved_at, resolution_category,
                 resolution_note, closed_at, version)
            VALUES (@id, @monitor_id, @owner_subject_id, @issue_key, 'Warning', 'Resolved', 0, now(), now(), now(),
                    'Fixed', 'Root cause addressed', now(), 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("monitor_id", monitorId);
        command.Parameters.AddWithValue("owner_subject_id", ownerSubjectId);
        command.Parameters.AddWithValue("issue_key", $"v1|HttpAvailability|closed-while-resolved|{Guid.NewGuid():N}");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("ck_incident_closed_fields");
    }

    private static async Task VerifyIncidentEventFieldsRejectedAsync(string connectionString, Guid incidentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO web_health.incident_event
                (id, incident_id, sequence_number, event_type, bounded_note, occurred_at)
            VALUES (@id, @incident_id, 2, 'NoteAdded', '', now());
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("incident_id", incidentId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("ck_incident_event_fields");
    }

    private static async Task VerifyEndpointHealthCrossMonitorRejectedAsync(
        string connectionString, Guid monitorId, Guid otherMonitorId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var otherCheckId = Guid.NewGuid();
        const string checkSql = """
            INSERT INTO web_health.logical_check
                (id, endpoint_monitor_id, source, requested_at, state, policy_fingerprint, created_at)
            VALUES (@id, @monitor_id, 'Urgent', now(), 'Pending', repeat('0', 64), now());
            """;
        await using (var check = new NpgsqlCommand(checkSql, connection))
        {
            check.Parameters.AddWithValue("id", otherCheckId);
            check.Parameters.AddWithValue("monitor_id", otherMonitorId);
            await check.ExecuteNonQueryAsync();
        }

        const string updateSql = """
            UPDATE web_health.endpoint_health
            SET evidence_logical_check_id = @check_id
            WHERE endpoint_monitor_id = @monitor_id;
            """;
        await using var update = new NpgsqlCommand(updateSql, connection);
        update.Parameters.AddWithValue("check_id", otherCheckId);
        update.Parameters.AddWithValue("monitor_id", monitorId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("fk_endpoint_health_logical_check_monitor");
    }

    private static async Task VerifyIncidentEvidenceCrossMonitorRejectedAsync(
        string connectionString, Guid incidentId, Guid monitorId, Guid otherMonitorId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var otherCheckId = Guid.NewGuid();
        const string checkSql = """
            INSERT INTO web_health.logical_check
                (id, endpoint_monitor_id, source, requested_at, state, policy_fingerprint, created_at)
            VALUES (@id, @monitor_id, 'Urgent', now(), 'Pending', repeat('0', 64), now());
            """;
        await using (var check = new NpgsqlCommand(checkSql, connection))
        {
            check.Parameters.AddWithValue("id", otherCheckId);
            check.Parameters.AddWithValue("monitor_id", otherMonitorId);
            await check.ExecuteNonQueryAsync();
        }

        const string evidenceSql = """
            INSERT INTO web_health.incident_evidence
                (id, incident_id, endpoint_monitor_id, logical_check_id, evidence_type, evidence_role,
                 bounded_snapshot, captured_at)
            VALUES (@id, @incident_id, @monitor_id, @check_id, 'Opening', 'CheckResult', '{}', now());
            """;
        await using var evidence = new NpgsqlCommand(evidenceSql, connection);
        evidence.Parameters.AddWithValue("id", Guid.NewGuid());
        evidence.Parameters.AddWithValue("incident_id", incidentId);
        evidence.Parameters.AddWithValue("monitor_id", monitorId);
        evidence.Parameters.AddWithValue("check_id", otherCheckId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => evidence.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("fk_incident_evidence_logical_check_monitor");
    }

    private static async Task VerifyMaintenanceOccurrenceImmutableAsync(string connectionString, Guid occurrenceId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.maintenance_occurrence SET ends_at = ends_at + interval '1 hour' WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", occurrenceId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.RaiseException);
    }

    private static async Task VerifyCheckResultMaintenanceIntervalRejectedAsync(
        string connectionString, Guid occurrenceId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string sql = """
            UPDATE web_health.check_result
            SET maintenance_occurrence_id = @occurrence_id, is_maintenance = TRUE
            WHERE logical_check_id = (SELECT logical_check_id FROM web_health.check_result LIMIT 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("occurrence_id", occurrenceId);
        await command.ExecuteNonQueryAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.ConstraintName.Should().Be("ck_check_result_maintenance_interval");
    }

    private static async Task VerifyDuplicateIncidentEventSequenceRejectedAsync(
        string connectionString, Guid incidentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO web_health.incident_event
                (id, incident_id, sequence_number, event_type, to_status, occurred_at)
            VALUES (@id, @incident_id, 1, 'Opened', 'Open', now());
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("incident_id", incidentId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        exception.ConstraintName.Should().Be("ix_incident_event_incident_id_sequence_number");
    }

    private static async Task VerifyIncidentEventImmutableAsync(string connectionString, Guid incidentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.incident_event SET bounded_note = 'edited' WHERE incident_id = @incident_id",
            connection);
        command.Parameters.AddWithValue("incident_id", incidentId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.RaiseException);
    }

    private static async Task VerifyIncidentEvidenceImmutableAsync(string connectionString, Guid evidenceId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM web_health.incident_evidence WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", evidenceId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.RaiseException);
    }

    private static async Task VerifyMaintenanceTargetScopeRejectedAsync(
        string connectionString, Guid windowId, Guid monitorId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO web_health.maintenance_target
                (id, maintenance_window_id, endpoint_monitor_id, environment_id)
            VALUES (@id, @window_id, @monitor_id, gen_random_uuid());
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("window_id", windowId);
        command.Parameters.AddWithValue("monitor_id", monitorId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("ck_maintenance_target_exactly_one_scope");
    }

    private static async Task VerifyMaintenanceOccurrenceIntervalRejectedAsync(
        string connectionString, Guid windowId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT INTO web_health.maintenance_occurrence
                (id, maintenance_window_id, starts_at, ends_at, created_at)
            VALUES (@id, @window_id, now(), now(), now());
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("window_id", windowId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("ck_maintenance_occurrence_interval");
    }

    private static async Task VerifyCheckResultMaintenanceFieldGroupRejectedAsync(
        string connectionString, Guid occurrenceId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            UPDATE web_health.check_result
            SET maintenance_occurrence_id = @occurrence_id, is_maintenance = FALSE
            WHERE logical_check_id = (SELECT logical_check_id FROM web_health.check_result LIMIT 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("occurrence_id", occurrenceId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("ck_check_result_maintenance");
    }

    private static async Task VerifyClientWebsiteRegistryAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebHealth"] = connectionString
            })
            .Build();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
        var clients = scope.ServiceProvider.GetRequiredService<IClientRegistryService>();
        var websites = scope.ServiceProvider.GetRequiredService<IWebsiteRegistryService>();
        var reader = scope.ServiceProvider.GetRequiredService<IRegistryReader>();

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var administratorOwnerId = await database.OwnerSubjects
            .Where(owner => owner.UserId == administrator.Id)
            .Select(owner => owner.Id)
            .SingleAsync();
        var administratorAccess = new RegistryAccessContext(
            administrator.Id,
            [ApplicationRoles.Administrator]);

        var developerResult = await users.CreateUserAsync(
            new CreateManagedUser(
                "Registry Developer",
                "registry-developer@example.test",
                $"Registry-9!{Guid.NewGuid():N}",
                [ApplicationRoles.DeveloperSupport]),
            administrator.Id);
        developerResult.Succeeded.Should().BeTrue(string.Join(" ", developerResult.Errors));
        var viewerResult = await users.CreateUserAsync(
            new CreateManagedUser(
                "Registry Viewer",
                "registry-viewer@example.test",
                $"Registry-9!{Guid.NewGuid():N}",
                [ApplicationRoles.Viewer]),
            administrator.Id);
        viewerResult.Succeeded.Should().BeTrue(string.Join(" ", viewerResult.Errors));

        var developer = await database.Users.SingleAsync(user => user.Email == "registry-developer@example.test");
        var viewer = await database.Users.SingleAsync(user => user.Email == "registry-viewer@example.test");
        var developerOwnerId = await database.OwnerSubjects
            .Where(owner => owner.UserId == developer.Id)
            .Select(owner => owner.Id)
            .SingleAsync();

        var firstClient = await clients.CreateAsync(
            new CreateClient("  Alpha Client  ", developerOwnerId, "  scoped notes  "),
            administratorAccess);
        firstClient.Succeeded.Should().BeTrue(string.Join(" ", firstClient.Errors));
        var firstClientId = firstClient.EntityId ?? throw new InvalidOperationException("Client id was not returned.");
        var duplicateClient = await clients.CreateAsync(
            new CreateClient("alpha client", administratorOwnerId, null),
            administratorAccess);
        duplicateClient.Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var secondClient = await clients.CreateAsync(
            new CreateClient("Second Client", administratorOwnerId, null),
            administratorAccess);
        secondClient.Succeeded.Should().BeTrue(string.Join(" ", secondClient.Errors));
        var secondClientId = secondClient.EntityId ?? throw new InvalidOperationException("Client id was not returned.");

        var persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        persistedClient.Name.Should().Be("Alpha Client");
        persistedClient.Notes.Should().Be("scoped notes");
        var staleClientUpdate = await clients.UpdateAsync(
            new UpdateClient(
                persistedClient.Id,
                persistedClient.Name,
                persistedClient.OwnerSubjectId,
                persistedClient.Notes,
                true,
                0),
            administratorAccess);
        staleClientUpdate.Status.Should().Be(RegistryMutationStatus.ConcurrencyConflict);

        var firstWebsite = await websites.CreateAsync(
            new CreateWebsite(firstClientId, "  Portal  ", developerOwnerId, " ASP.NET ", false,
                ["  Europe ", "ASP.NET", "europe"]),
            administratorAccess);
        firstWebsite.Succeeded.Should().BeTrue(string.Join(" ", firstWebsite.Errors));
        var firstWebsiteId = firstWebsite.EntityId ?? throw new InvalidOperationException("Website id was not returned.");
        (await websites.CreateAsync(
            new CreateWebsite(firstClientId, "portal", developerOwnerId, null, false, []),
            administratorAccess)).Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        (await websites.CreateAsync(
            new CreateWebsite(secondClientId, "Portal", administratorOwnerId, null, false, []),
            administratorAccess)).Succeeded.Should().BeTrue();

        var persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var enableWithoutEnvironment = await websites.UpdateAsync(
            new UpdateWebsite(
                persistedWebsite.Id,
                persistedWebsite.Name,
                persistedWebsite.OwnerSubjectId,
                persistedWebsite.TechnologyCms,
                true,
                persistedWebsite.Version,
                ["ASP.NET", "Europe"]),
            administratorAccess);
        enableWithoutEnvironment.Status.Should().Be(RegistryMutationStatus.ValidationFailed);

        await VerifyEnabledWebsiteConstraintAsync(connectionString, persistedWebsite.Id);
        database.ChangeTracker.Clear();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var now = DateTimeOffset.UtcNow;
        database.Environments.Add(new WebsiteEnvironment
        {
            Id = Guid.NewGuid(),
            WebsiteId = persistedWebsite.Id,
            Name = "Production",
            NormalizedName = "production",
            NormalizationVersion = 1,
            EnvironmentType = "Production",
            IsProduction = true,
            BaseUrl = "https://example.test",
            IsActive = true,
            CreatedAt = now,
            CreatedByUserId = administrator.Id,
            UpdatedAt = now,
            UpdatedByUserId = administrator.Id,
            Version = 1
        });
        await database.SaveChangesAsync();
        (await websites.UpdateAsync(
            new UpdateWebsite(
                persistedWebsite.Id,
                persistedWebsite.Name,
                persistedWebsite.OwnerSubjectId,
                persistedWebsite.TechnologyCms,
                true,
                persistedWebsite.Version,
                ["ASP.NET", "Europe"]),
            administratorAccess)).Succeeded.Should().BeTrue();

        database.AccessGrants.Add(new AccessGrant
        {
            Id = Guid.NewGuid(),
            UserId = viewer.Id,
            AccessLevel = "Read",
            ClientId = firstClientId,
            EffectiveFrom = now.AddMinutes(-1),
            CreatedAt = now,
            CreatedByUserId = administrator.Id
        });
        await database.SaveChangesAsync();

        var developerClients = await reader.ListClientsAsync(new(
            developer.Id,
            [ApplicationRoles.DeveloperSupport]));
        developerClients.Select(client => client.Id).Should().Equal(firstClientId);
        database.ChangeTracker.Clear();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        (await websites.UpdateAsync(
            new UpdateWebsite(
                persistedWebsite.Id,
                persistedWebsite.Name,
                persistedWebsite.OwnerSubjectId,
                persistedWebsite.TechnologyCms,
                persistedWebsite.IsEnabled,
                persistedWebsite.Version,
                ["ASP.NET", "Europe"]),
            new RegistryAccessContext(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Status.Should().Be(RegistryMutationStatus.Forbidden);
        var viewerWebsites = await reader.ListWebsitesAsync(new(viewer.Id, [ApplicationRoles.Viewer]));
        viewerWebsites.Should().Contain(website => website.Id == firstWebsiteId);
        viewerWebsites.Should().NotContain(website => website.ClientId == secondClientId);
        viewerWebsites.Single(website => website.Id == firstWebsiteId).Tags.Should().Equal("ASP.NET", "Europe");
        var europeTag = await database.Tags.SingleAsync(tag => tag.NormalizedName == "EUROPE");
        (await database.Tags.CountAsync()).Should().Be(2, "repeated tag input is normalized and stored once");
        (await reader.ListWebsitesAsync(
            new(viewer.Id, [ApplicationRoles.Viewer]), europeTag.Id))
            .Should().ContainSingle(website => website.Id == firstWebsiteId);
        (await reader.ListTagsAsync(new(viewer.Id, [ApplicationRoles.Viewer])))
            .Should().ContainSingle(tag => tag.Id == europeTag.Id && tag.WebsiteCount == 1);
        await VerifyTagUniquenessConstraintAsync(connectionString, europeTag.Id);

        var websiteAudit = await database.AuditEvents.AsNoTracking()
            .Where(auditEvent => auditEvent.EntityIdentifier == firstWebsiteId.ToString())
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .FirstAsync();
        websiteAudit.AfterValues.Should().Contain("ASP.NET").And.Contain("Europe");

        var startConcurrentTagWrites = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentCreates = new[]
        {
            CreateWebsiteWithSharedTagAsync(
                connectionString, firstClientId, "Concurrent Alpha", developerOwnerId,
                administratorAccess, startConcurrentTagWrites.Task),
            CreateWebsiteWithSharedTagAsync(
                connectionString, secondClientId, "Concurrent Beta", administratorOwnerId,
                administratorAccess, startConcurrentTagWrites.Task)
        };
        startConcurrentTagWrites.SetResult();
        var concurrentResults = await Task.WhenAll(concurrentCreates);
        concurrentResults.Should().OnlyContain(result => result.Succeeded);
        var concurrentTag = await database.Tags.SingleAsync(tag => tag.NormalizedName == "CONCURRENT");
        (await database.WebsiteTags.CountAsync(websiteTag => websiteTag.TagId == concurrentTag.Id))
            .Should().Be(2);

        database.ChangeTracker.Clear();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var disabled = await websites.DisableAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess);
        disabled.Succeeded.Should().BeTrue(string.Join(" ", disabled.Errors));
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var deleted = await websites.DeleteAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess);
        deleted.Succeeded.Should().BeTrue(string.Join(" ", deleted.Errors));
        (await reader.ListWebsitesAsync(administratorAccess))
            .Should().NotContain(website => website.Id == firstWebsiteId);
        (await reader.ListDeletedWebsitesAsync(administratorAccess))
            .Should().ContainSingle(website => website.Id == firstWebsiteId);
        (await reader.ListDeletedWebsitesAsync(new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().BeEmpty();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var replacementWebsite = await websites.CreateAsync(
            new CreateWebsite(
                firstClientId,
                persistedWebsite.Name,
                persistedWebsite.OwnerSubjectId,
                null,
                false,
                []),
            administratorAccess);
        replacementWebsite.Succeeded.Should().BeTrue(string.Join(" ", replacementWebsite.Errors));
        (await websites.RestoreAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var replacementWebsiteEntity = await database.Websites.SingleAsync(website =>
            website.Id == replacementWebsite.EntityId);
        (await websites.DeleteAsync(
            new(replacementWebsiteEntity.Id, replacementWebsiteEntity.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var restored = await websites.RestoreAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess);
        restored.Succeeded.Should().BeTrue(string.Join(" ", restored.Errors));

        var websiteAuditActions = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == persistedWebsite.Id.ToString())
            .Select(audit => audit.Action)
            .ToListAsync();
        websiteAuditActions.Should().Contain([
            "website.created",
            "website.updated",
            "website.disabled",
            "website.deleted",
            "website.restored"]);

        database.ChangeTracker.Clear();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.UpdateAsync(
            new UpdateClient(
                persistedClient.Id,
                persistedClient.Name,
                persistedClient.OwnerSubjectId,
                "changed private notes",
                true,
                persistedClient.Version),
            administratorAccess)).Succeeded.Should().BeTrue();
        var notesAuditJson = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == firstClientId.ToString()
                && audit.Action == "client.updated")
            .OrderByDescending(audit => audit.OccurredAt)
            .Select(audit => audit.AfterValues)
            .FirstAsync();
        using (var notesAudit = JsonDocument.Parse(notesAuditJson!))
        {
            notesAudit.RootElement.GetProperty("notesChanged").GetBoolean().Should().BeTrue();
            notesAuditJson.Should().NotContain("changed private notes");
        }

        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.DisableAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.DeleteAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        (await reader.ListClientsAsync(administratorAccess))
            .Should().NotContain(client => client.Id == firstClientId);
        (await reader.ListDeletedClientsAsync(administratorAccess))
            .Should().ContainSingle(client => client.Id == firstClientId);
        (await reader.ListDeletedClientsAsync(new(viewer.Id, [ApplicationRoles.Viewer])))
            .Should().BeEmpty();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        var replacementClient = await clients.CreateAsync(
            new CreateClient(persistedClient.Name, persistedClient.OwnerSubjectId, null),
            administratorAccess);
        replacementClient.Succeeded.Should().BeTrue(string.Join(" ", replacementClient.Errors));
        (await clients.RestoreAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var replacementClientEntity = await database.Clients.SingleAsync(client =>
            client.Id == replacementClient.EntityId);
        (await clients.DeleteAsync(
            new(replacementClientEntity.Id, replacementClientEntity.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.RestoreAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Succeeded.Should().BeTrue();

        var clientAuditActions = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == firstClientId.ToString())
            .Select(audit => audit.Action)
            .ToListAsync();
        clientAuditActions.Should().Contain([
            "client.created",
            "client.updated",
            "client.disabled",
            "client.deleted",
            "client.restored"]);
    }

    private static async Task VerifyEnabledWebsiteConstraintAsync(
        string connectionString,
        Guid websiteId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.website SET is_enabled = TRUE WHERE id = @id",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", websiteId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.Should().Be("ck_website_enabled_requires_active_environment");
    }

    private static async Task<RegistryMutationResult> CreateWebsiteWithSharedTagAsync(
        string connectionString,
        Guid clientId,
        string websiteName,
        Guid ownerSubjectId,
        RegistryAccessContext access,
        Task start)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var websiteService = scope.ServiceProvider.GetRequiredService<IWebsiteRegistryService>();
        await start;
        return await websiteService.CreateAsync(
            new CreateWebsite(clientId, websiteName, ownerSubjectId, null, false, ["Concurrent"]),
            access);
    }

    private static async Task VerifyTagUniquenessConstraintAsync(string connectionString, Guid tagId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO web_health.tag
                (id, name, normalized_name, normalization_version, created_at, created_by_user_id, version)
            SELECT gen_random_uuid(), name, normalized_name, normalization_version, now(), created_by_user_id, 1
            FROM web_health.tag
            WHERE id = @tag_id;
            """;
        command.Parameters.AddWithValue("tag_id", tagId);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("ix_tag_normalized_name_normalization_version");
    }

    private static async Task VerifyIdentityBootstrapAsync(string connectionString)
    {
        var password = $"Integration-9!{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebHealth"] = connectionString,
                ["BootstrapAdmin:Email"] = "bootstrap@example.test",
                ["BootstrapAdmin:DisplayName"] = "Bootstrap Administrator",
                ["BootstrapAdmin:Password"] = password
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
            await bootstrapper.BootstrapAsync();
            await bootstrapper.BootstrapAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var roles = await roleManager.Roles.OrderBy(role => role.Name).ToListAsync();
            roles.Select(role => (role.Id, role.Name)).Should().BeEquivalentTo(
                ApplicationRoles.All.Select(role => (role.Id, (string?)role.Name)));

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync("bootstrap@example.test");
            user.Should().NotBeNull();
            user!.PasswordHash.Should().NotBeNullOrWhiteSpace().And.NotBe(password);
            (await userManager.CheckPasswordAsync(user, password)).Should().BeTrue();
            (await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator)).Should().BeTrue();

            var signInManager = scope.ServiceProvider
                .GetRequiredService<SignInManager<ApplicationUser>>();
            user.IsDisabled = true;
            (await signInManager.CanSignInAsync(user)).Should().BeFalse();
            user.IsDisabled = false;

            var administration = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
            var managedPassword = $"Managed-8!{Guid.NewGuid():N}";
            var createResult = await administration.CreateUserAsync(
                new CreateManagedUser(
                    "Managed Viewer",
                    "managed-viewer@example.test",
                    managedPassword,
                    [ApplicationRoles.Viewer]),
                user.Id);
            createResult.Succeeded.Should().BeTrue(string.Join(" ", createResult.Errors));

            var managedUser = await userManager.FindByIdAsync(createResult.UserId!.Value.ToString());
            managedUser.Should().NotBeNull();
            var originalSecurityStamp = managedUser!.SecurityStamp;
            var existingPrincipal = await signInManager.CreateUserPrincipalAsync(managedUser);
            var replacementPassword = $"Replacement-7!{Guid.NewGuid():N}";

            var disableResult = await administration.UpdateUserAsync(
                new UpdateManagedUser(
                    managedUser.Id,
                    "Managed Viewer",
                    true,
                    [ApplicationRoles.Operations],
                    replacementPassword),
                user.Id);
            disableResult.Succeeded.Should().BeTrue(string.Join(" ", disableResult.Errors));

            managedUser = await userManager.FindByIdAsync(managedUser.Id.ToString());
            managedUser!.IsDisabled.Should().BeTrue();
            managedUser.SecurityStamp.Should().NotBe(originalSecurityStamp);
            (await userManager.GetRolesAsync(managedUser)).Should().Equal(ApplicationRoles.Operations);
            (await userManager.CheckPasswordAsync(managedUser, replacementPassword)).Should().BeTrue();
            managedUser.PasswordHash.Should().NotBe(replacementPassword);
            (await signInManager.ValidateSecurityStampAsync(existingPrincipal)).Should().BeNull();

            var selfDisableResult = await administration.UpdateUserAsync(
                new UpdateManagedUser(
                    user.Id,
                    user.DisplayName,
                    true,
                    [ApplicationRoles.Administrator]),
                user.Id);
            selfDisableResult.Succeeded.Should().BeFalse();

            var roleOnlyPassword = $"Role-only-6!{Guid.NewGuid():N}";
            var roleOnlyCreateResult = await administration.CreateUserAsync(
                new CreateManagedUser(
                    "Role-only Administrator",
                    "role-only@example.test",
                    roleOnlyPassword,
                    [ApplicationRoles.Administrator]),
                user.Id);
            roleOnlyCreateResult.Succeeded.Should().BeTrue(string.Join(" ", roleOnlyCreateResult.Errors));

            var roleOnlyUser = await userManager.FindByIdAsync(roleOnlyCreateResult.UserId!.Value.ToString());
            var roleOnlyPrincipal = await signInManager.CreateUserPrincipalAsync(roleOnlyUser!);
            var roleOnlySecurityStamp = roleOnlyUser!.SecurityStamp;
            var roleOnlyUpdateResult = await administration.UpdateUserAsync(
                new UpdateManagedUser(
                    roleOnlyUser.Id,
                    roleOnlyUser.DisplayName,
                    false,
                    [ApplicationRoles.Viewer]),
                user.Id);
            roleOnlyUpdateResult.Succeeded.Should().BeTrue(string.Join(" ", roleOnlyUpdateResult.Errors));

            roleOnlyUser = await userManager.FindByIdAsync(roleOnlyUser.Id.ToString());
            roleOnlyUser!.SecurityStamp.Should().NotBe(roleOnlySecurityStamp);
            (await signInManager.ValidateSecurityStampAsync(roleOnlyPrincipal)).Should().BeNull();

            var teamAdministration = scope.ServiceProvider.GetRequiredService<ITeamAdministrationService>();
            var createTeamResult = await teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("  Platform   Support  ", [roleOnlyUser.Id]),
                user.Id);
            createTeamResult.Succeeded.Should().BeTrue(string.Join(" ", createTeamResult.Errors));
            var team = await teamAdministration.FindTeamAsync(createTeamResult.TeamId!.Value);
            team.Should().NotBeNull();
            team!.Name.Should().Be("Platform Support");
            team.Members.Select(member => member.UserId).Should().Equal(roleOnlyUser.Id);
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var teamOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.TeamId == team.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            var assignmentAccess = scope.ServiceProvider.GetRequiredService<IAssignmentAccessEvaluator>();
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                teamOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeTrue();

            var duplicateTeamResult = await teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("platform support", []),
                user.Id);
            duplicateTeamResult.Succeeded.Should().BeFalse();

            var disabledTeamResult = await teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("Disabled Team", [roleOnlyUser.Id]),
                user.Id);
            disabledTeamResult.Succeeded.Should().BeTrue(string.Join(" ", disabledTeamResult.Errors));
            var disabledTeam = await teamAdministration.FindTeamAsync(disabledTeamResult.TeamId!.Value);
            var disabledTeamOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.TeamId == disabledTeam!.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            (await teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(
                    disabledTeam!.Id,
                    disabledTeam.Name,
                    true,
                    disabledTeam.Version,
                    [roleOnlyUser.Id]),
                user.Id)).Succeeded.Should().BeTrue();
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                disabledTeamOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeFalse();

            var updateTeamResult = await teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(team.Id, "Platform Reliability", false, team.Version, []),
                user.Id);
            updateTeamResult.Succeeded.Should().BeTrue(string.Join(" ", updateTeamResult.Errors));
            var listedTeams = await teamAdministration.ListTeamsAsync();
            listedTeams.Should().ContainSingle(candidate =>
                candidate.Id == team.Id
                && candidate.Name == "Platform Reliability"
                && candidate.Members.Count == 0);
            var staleTeamResult = await teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(team.Id, "Stale update", false, team.Version, []),
                user.Id);
            staleTeamResult.Succeeded.Should().BeFalse();

            (await database.OwnerSubjects.CountAsync(subject => subject.TeamId == team.Id)).Should().Be(1);
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                teamOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeFalse();
            var closedMembership = await database.TeamMembers.SingleAsync(member => member.TeamId == team.Id);
            closedMembership.EffectiveUntil.Should().NotBeNull();
            await VerifyMembershipPeriodsCannotOverlapAsync(
                connectionString,
                closedMembership,
                user.Id);

            var auditTrail = scope.ServiceProvider.GetRequiredService<IAuditTrailReader>();
            var teamAudit = await auditTrail.SearchAsync(new AuditSearchQuery(
                ActorUserId: user.Id,
                Action: "team.updated",
                Entity: team.Id.ToString()));
            teamAudit.TotalCount.Should().Be(1);
            teamAudit.Events[0].BeforeValues.Should().ContainKey("memberUserIds");
            teamAudit.Events[0].AfterValues.Should().ContainKey("memberUserIds");
            var boundedAuditPage = await auditTrail.SearchAsync(new AuditSearchQuery(
                ToDate: DateOnly.MaxValue,
                Page: int.MaxValue));
            boundedAuditPage.Page.Should().Be(1);
            boundedAuditPage.Events.Should().NotBeEmpty();
            var userAudit = await auditTrail.SearchAsync(new AuditSearchQuery(
                ActorUserId: user.Id,
                Action: "user.updated",
                Entity: managedUser.Id.ToString()));
            userAudit.TotalCount.Should().Be(1);
            userAudit.Events[0].AfterValues["passwordReset"].Should().Be("true");
            userAudit.Events[0].AfterValues.Values.Should().NotContain(replacementPassword);
            (await database.OwnerSubjects.AnyAsync(subject => subject.UserId == roleOnlyUser.Id))
                .Should().BeTrue();
            var userOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.UserId == roleOnlyUser.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                userOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeTrue();
            var disabledUserOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.UserId == managedUser.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            (await assignmentAccess.IsAssignedAsync(
                managedUser.Id,
                disabledUserOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeFalse();

            await VerifyDisabledMembershipRetentionAsync(scope.ServiceProvider, user);
            await VerifyConcurrentDisableIsObservedAsync(services, user, connectionString);

            var auditWriter = scope.ServiceProvider.GetRequiredService<IAuthorizationDenialAuditWriter>();
            await auditWriter.WriteAsync(new AuthorizationDenialAuditEntry(
                user.Id,
                DateTimeOffset.UtcNow,
                "GET",
                "/Administration/Users",
                "database-foundation-correlation"));
            var auditEvent = await database
                .AuditEvents
                .SingleAsync(candidate => candidate.Action == "authorization.denied");
            auditEvent.ActorUserId.Should().Be(user.Id);
            auditEvent.Action.Should().Be("authorization.denied");
            auditEvent.EntityIdentifier.Should().Be("/Administration/Users");
            auditEvent.Outcome.Should().Be("forbidden");

            await VerifyAuditRowsAreAppendOnlyAsync(connectionString, auditEvent.Id);
        }
    }

    private static async Task VerifyDisabledMembershipRetentionAsync(
        IServiceProvider services,
        ApplicationUser administrator)
    {
        var userAdministration = services.GetRequiredService<IUserAdministrationService>();
        var teamAdministration = services.GetRequiredService<ITeamAdministrationService>();
        var database = services.GetRequiredService<ApplicationDbContext>();
        var password = $"Retained-5!{Guid.NewGuid():N}";
        var createUser = await userAdministration.CreateUserAsync(
            new CreateManagedUser(
                "Retained Disabled Member",
                "retained-disabled@example.test",
                password,
                [ApplicationRoles.Viewer]),
            administrator.Id);
        createUser.Succeeded.Should().BeTrue(string.Join(" ", createUser.Errors));
        var memberId = createUser.UserId!.Value;
        var createTeam = await teamAdministration.CreateTeamAsync(
            new CreateManagedTeam("Retention Team", [memberId]),
            administrator.Id);
        createTeam.Succeeded.Should().BeTrue(string.Join(" ", createTeam.Errors));

        var disableUser = await userAdministration.UpdateUserAsync(
            new UpdateManagedUser(
                memberId,
                "Retained Disabled Member",
                true,
                [ApplicationRoles.Viewer]),
            administrator.Id);
        disableUser.Succeeded.Should().BeTrue(string.Join(" ", disableUser.Errors));
        var team = await teamAdministration.FindTeamAsync(createTeam.TeamId!.Value);
        var renameTeam = await teamAdministration.UpdateTeamAsync(
            new UpdateManagedTeam(
                team!.Id,
                "Renamed Retention Team",
                false,
                team.Version,
                [memberId]),
            administrator.Id);
        renameTeam.Succeeded.Should().BeTrue(string.Join(" ", renameTeam.Errors));
        (await database.TeamMembers.SingleAsync(member => member.TeamId == team.Id))
            .EffectiveUntil.Should().BeNull();

        var addDisabledUser = await teamAdministration.CreateTeamAsync(
            new CreateManagedTeam("Rejected Disabled Assignment", [memberId]),
            administrator.Id);
        addDisabledUser.Succeeded.Should().BeFalse();
    }

    private static async Task VerifyConcurrentDisableIsObservedAsync(
        IServiceProvider rootServices,
        ApplicationUser administrator,
        string connectionString)
    {
        Guid createUserId;
        Guid updateUserId;
        await using (var setupScope = rootServices.CreateAsyncScope())
        {
            var userAdministration = setupScope.ServiceProvider
                .GetRequiredService<IUserAdministrationService>();
            var createUser = await userAdministration.CreateUserAsync(
                new CreateManagedUser(
                    "Concurrent Create User",
                    "concurrent-create@example.test",
                    $"Concurrent-4!{Guid.NewGuid():N}",
                    [ApplicationRoles.Viewer]),
                administrator.Id);
            createUser.Succeeded.Should().BeTrue(string.Join(" ", createUser.Errors));
            createUserId = createUser.UserId!.Value;
            var updateUser = await userAdministration.CreateUserAsync(
                new CreateManagedUser(
                    "Concurrent Update User",
                    "concurrent-update@example.test",
                    $"Concurrent-3!{Guid.NewGuid():N}",
                    [ApplicationRoles.Viewer]),
                administrator.Id);
            updateUser.Succeeded.Should().BeTrue(string.Join(" ", updateUser.Errors));
            updateUserId = updateUser.UserId!.Value;
        }

        await using var assignmentScope = rootServices.CreateAsyncScope();
        var teamAdministration = assignmentScope.ServiceProvider
            .GetRequiredService<ITeamAdministrationService>();
        var createResult = await RunDuringConcurrentDisableAsync(
            connectionString,
            createUserId,
            () => teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("Concurrent Create Team", [createUserId]),
                administrator.Id));
        createResult.Succeeded.Should().BeFalse();

        var emptyTeamResult = await teamAdministration.CreateTeamAsync(
            new CreateManagedTeam("Concurrent Update Team", []),
            administrator.Id);
        emptyTeamResult.Succeeded.Should().BeTrue(string.Join(" ", emptyTeamResult.Errors));
        var emptyTeam = await teamAdministration.FindTeamAsync(emptyTeamResult.TeamId!.Value);
        var updateResult = await RunDuringConcurrentDisableAsync(
            connectionString,
            updateUserId,
            () => teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(
                    emptyTeam!.Id,
                    emptyTeam.Name,
                    false,
                    emptyTeam.Version,
                    [updateUserId]),
                administrator.Id));
        updateResult.Succeeded.Should().BeFalse();
    }

    private static async Task<TeamAdministrationResult> RunDuringConcurrentDisableAsync(
        string connectionString,
        Guid userId,
        Func<Task<TeamAdministrationResult>> mutation)
    {
        await using var blockingConnection = new NpgsqlConnection(connectionString);
        await blockingConnection.OpenAsync();
        await using var blockingTransaction = await blockingConnection.BeginTransactionAsync();
        await using (var disableCommand = new NpgsqlCommand(
                         "UPDATE web_health.app_user SET is_disabled = TRUE WHERE id = @id",
                         blockingConnection,
                         blockingTransaction))
        {
            disableCommand.Parameters.AddWithValue("id", userId);
            (await disableCommand.ExecuteNonQueryAsync()).Should().Be(1);
        }

        var mutationTask = mutation();
        await Task.Delay(200);
        mutationTask.IsCompleted.Should().BeFalse();

        await blockingTransaction.CommitAsync();
        return await mutationTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task VerifyAuditRowsAreAppendOnlyAsync(
        string connectionString,
        Guid auditEventId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.audit_event SET outcome = 'changed' WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", auditEventId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.RaiseException);
    }

    private static async Task VerifyMembershipPeriodsCannotOverlapAsync(
        string connectionString,
        TeamMember existingMembership,
        Guid actorUserId)
    {
        const string sql = """
            INSERT INTO web_health.team_member
                (id, team_id, user_id, effective_from, effective_until, created_at, created_by_user_id)
            VALUES
                (@id, @team_id, @user_id, @effective_from, @effective_until, @created_at, @actor_user_id);
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("team_id", existingMembership.TeamId);
        command.Parameters.AddWithValue("user_id", existingMembership.UserId);
        command.Parameters.AddWithValue("effective_from", existingMembership.EffectiveFrom);
        command.Parameters.AddWithValue("effective_until", existingMembership.EffectiveUntil!.Value);
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
    }

    private static async Task<(bool SchemaExists, IReadOnlyList<string> Tables)> ReadFoundationState(
        string connectionString)
    {
        const string sql = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'web_health'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return (tables.Count > 0, tables);
    }

    private sealed class RecordingLogicalCheckQueue : ILogicalCheckQueue
    {
        private readonly ConcurrentQueue<(Guid LogicalCheckId, Guid DurableWorkId)> jobs = new();

        public bool FailNext { get; set; }

        /// <summary>
        /// Invoked synchronously inside Enqueue, before this method returns, so a test can simulate
        /// a worker racing ahead of the caller's own enqueue-acknowledgement step (e.g. advancing the
        /// durable work row to Processing or Completed via a separate connection).
        /// </summary>
        public Action<Guid, Guid>? OnEnqueue { get; set; }

        public IReadOnlyList<(Guid LogicalCheckId, Guid DurableWorkId)> Jobs => jobs.ToArray();

        public string Enqueue(Guid logicalCheckId, Guid durableWorkId)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Simulated enqueue interruption.");
            }

            OnEnqueue?.Invoke(logicalCheckId, durableWorkId);
            jobs.Enqueue((logicalCheckId, durableWorkId));
            return jobs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
