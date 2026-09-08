using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.PageAudits;
using WebHealth.Infrastructure.Seo;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class EndpointPurgeCascade(ApplicationDbContext dbContext)
{
    public async Task ExecuteAsync(Guid endpointId, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "SET LOCAL web_health.endpoint_purge = 'on'", cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM web_health.endpoint WHERE id = {endpointId} FOR UPDATE
            """, cancellationToken);

        var normalizedUrl = await dbContext.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.Id == endpointId)
            .Select(endpoint => endpoint.NormalizedUrl)
            .SingleOrDefaultAsync(cancellationToken);
        if (normalizedUrl is null)
        {
            return;
        }

        await dbContext.TargetAuthorizationEvidence.Where(item => item.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);

        var origin = RobotsRefreshService.OriginOf(normalizedUrl);

        await RobotsOriginLock.AcquireAsync(dbContext, origin, cancellationToken);

        var monitors = dbContext.EndpointMonitors
            .Where(monitor => monitor.EndpointId == endpointId).Select(monitor => monitor.Id);
        var checks = dbContext.LogicalChecks
            .Where(check => monitors.Contains(check.EndpointMonitorId)).Select(check => check.Id);
        var incidents = dbContext.Incidents
            .Where(incident => monitors.Contains(incident.EndpointMonitorId)).Select(incident => incident.Id);
        var notifications = dbContext.NotificationEvents
            .Where(notification => incidents.Contains(notification.IncidentId)).Select(notification => notification.Id);
        var deliveries = dbContext.NotificationDeliveries
            .Where(delivery => notifications.Contains(delivery.NotificationEventId)).Select(delivery => delivery.Id);

        await dbContext.NotificationAttempts
            .Where(attempt => deliveries.Contains(attempt.NotificationDeliveryId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.NotificationDeliveries
            .Where(delivery => notifications.Contains(delivery.NotificationEventId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.NotificationEvents
            .Where(notification => incidents.Contains(notification.IncidentId))
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.IncidentEvidence
            .Where(evidence => incidents.Contains(evidence.IncidentId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.IncidentEvents
            .Where(incidentEvent => incidents.Contains(incidentEvent.IncidentId))
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Incidents
            .Where(incident => incident.PreviousIncidentId != null
                && incidents.Contains(incident.PreviousIncidentId.Value))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(incident => incident.PreviousIncidentId, (Guid?)null),
                cancellationToken);
        await dbContext.Incidents
            .Where(incident => monitors.Contains(incident.EndpointMonitorId))
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.IssueStates
            .Where(state => monitors.Contains(state.EndpointMonitorId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.EndpointHealth
            .Where(health => monitors.Contains(health.EndpointMonitorId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ExecutionLeases
            .Where(lease => monitors.Contains(lease.EndpointMonitorId))
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.RedirectHops
            .Where(hop => checks.Contains(hop.LogicalCheckId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Findings
            .Where(finding => checks.Contains(finding.LogicalCheckId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.CheckResults
            .Where(result => checks.Contains(result.LogicalCheckId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.CertificateObservations
            .Where(observation => checks.Contains(observation.LogicalCheckId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.SeoObservations
            .Where(observation => checks.Contains(observation.LogicalCheckId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.CheckConfigurationSnapshots
            .Where(snapshot => checks.Contains(snapshot.LogicalCheckId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ExecutionAttempts
            .Where(attempt => checks.Contains(attempt.LogicalCheckId))
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.DurableWork
            .Where(work => checks.Contains(work.LogicalCheckId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.LogicalChecks
            .Where(check => monitors.Contains(check.EndpointMonitorId))
            .ExecuteDeleteAsync(cancellationToken);

        var pngAuditRuns = dbContext.PngAuditRuns
            .Where(run => run.EndpointId == endpointId).Select(run => run.Id);
        var pngImageResults = dbContext.PngAuditImageResults
            .Where(result => pngAuditRuns.Contains(result.RunId)).Select(result => result.Id);
        await dbContext.PngAuditImageSources
            .Where(source => pngImageResults.Contains(source.ImageResultId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PngAuditImageResults
            .Where(result => pngAuditRuns.Contains(result.RunId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PngAuditDiscoverySkips
            .Where(skip => pngAuditRuns.Contains(skip.RunId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PngAuditCoverageReasons
            .Where(reason => pngAuditRuns.Contains(reason.RunId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PngAuditRuns
            .Where(run => run.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);

        var pageAuditRuns = dbContext.PageAuditRuns
            .Where(run => run.EndpointId == endpointId).Select(run => run.Id);
        await dbContext.PageAuditItems
            .Where(item => pageAuditRuns.Contains(item.RunId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PageAuditRuns
            .Where(run => run.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PageAuditTargets
            .Where(target => target.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PageAuditIncidentPolicies
            .Where(policy => policy.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);

        var runs = dbContext.CrawlRuns
            .Where(run => run.EndpointId == endpointId).Select(run => run.Id);
        await dbContext.CrawlLinkResults
            .Where(link => runs.Contains(link.RunId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.CrawlRuns
            .Where(run => run.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);

        await MaintenanceScopePurge.RemoveTargetsAsync(
            dbContext,
            target => target.EndpointId == endpointId
                || (target.EndpointMonitorId != null && monitors.Contains(target.EndpointMonitorId.Value)),
            cancellationToken);

        await dbContext.AccessGrants
            .Where(grant => grant.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.MonitoringDailyAggregates.Where(aggregate => monitors.Contains(aggregate.EndpointMonitorId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.EndpointMonitors
            .Where(monitor => monitor.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Endpoints
            .Where(endpoint => endpoint.Id == endpointId)
            .ExecuteDeleteAsync(cancellationToken);

        var originStillInUse = await dbContext.Endpoints.AsNoTracking().AnyAsync(
            endpoint => endpoint.NormalizedUrl.StartsWith(origin + "/"),
            cancellationToken);
        if (!originStillInUse)
        {
            await dbContext.RobotsSnapshots
                .Where(snapshot => snapshot.Origin == origin)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
