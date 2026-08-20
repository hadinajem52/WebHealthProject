using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Seo;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

/// <summary>
/// Removes every row that exists only because one endpoint existed, in foreign-key order.
/// </summary>
/// <remarks>
/// <para>
/// Every foreign key in this schema is <c>RESTRICT</c> by convention, and PostgreSQL evaluates
/// <c>RESTRICT</c> per row rather than at the end of the statement. Nothing here can therefore be
/// collapsed into fewer statements: each delete must already have left no referencing row behind,
/// and the sequence below is the only order in which that holds.
/// </para>
/// <para>
/// The audit trail is deliberately absent from this cascade. <c>audit_event</c> keys the endpoint
/// by identifier rather than by foreign key, so it survives the purge and stays the record that
/// the endpoint existed and that somebody removed it.
/// </para>
/// <para>
/// <c>robots_snapshot</c> is the one row here that is not the endpoint's alone: it is keyed by
/// origin and shared by every endpoint on the host. It is therefore removed conditionally, once
/// the purge has taken the last endpoint on that origin with it.
/// </para>
/// </remarks>
internal sealed class EndpointPurgeCascade(ApplicationDbContext dbContext)
{
    public async Task ExecuteAsync(Guid endpointId, CancellationToken cancellationToken)
    {
        // check_configuration_snapshot, incident_event, incident_evidence and
        // maintenance_occurrence carry triggers that reject every update and every delete. This
        // is the one caller allowed past the delete half of that rule, and SET LOCAL scopes the
        // exemption to this transaction: it is gone whether the purge commits or rolls back.
        await dbContext.Database.ExecuteSqlRawAsync(
            "SET LOCAL web_health.endpoint_purge = 'on'", cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM web_health.endpoint WHERE id = {endpointId} FOR UPDATE
            """, cancellationToken);

        // Read before the endpoint row goes, because the question it answers - is this the last
        // endpoint on the origin - can only be asked once the row is gone.
        var normalizedUrl = await dbContext.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.Id == endpointId)
            .Select(endpoint => endpoint.NormalizedUrl)
            .SingleOrDefaultAsync(cancellationToken);
        if (normalizedUrl is null)
        {
            return;
        }

        var origin = RobotsRefreshService.OriginOf(normalizedUrl);

        await RobotsOriginLock.AcquireAsync(dbContext, origin, cancellationToken);

        // Each of these stays an unexecuted query, re-evaluated by the statement that uses it.
        // That is safe only because every statement below runs before the rows it selects over
        // are themselves deleted, which is what fixes the order of this method.
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

        // Read the affected windows before their endpoint and monitor targets are removed.
        var maintenanceWindowIds = await dbContext.MaintenanceTargets.AsNoTracking()
            .Where(target => target.EndpointId == endpointId
                || (target.EndpointMonitorId != null && monitors.Contains(target.EndpointMonitorId.Value)))
            .Select(target => target.MaintenanceWindowId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

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

        // Recurrence chains itself through incident.previous_incident_id, and a self-reference
        // under RESTRICT cannot be dropped by the same statement that deletes the row it points
        // at. Detaching the chain first is what lets the delete below run as one statement.
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

        // Queued and in-flight work goes with the check that owns it. Without this a recovery
        // sweep could still enqueue work for an endpoint that no longer exists.
        await dbContext.DurableWork
            .Where(work => checks.Contains(work.LogicalCheckId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.LogicalChecks
            .Where(check => monitors.Contains(check.EndpointMonitorId))
            .ExecuteDeleteAsync(cancellationToken);

        var runs = dbContext.CrawlRuns
            .Where(run => run.EndpointId == endpointId).Select(run => run.Id);
        await dbContext.CrawlLinkResults
            .Where(link => runs.Contains(link.RunId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.CrawlRuns
            .Where(run => run.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);

        if (maintenanceWindowIds.Length > 0)
        {
            await dbContext.MaintenanceTargets
                .Where(target => target.EndpointId == endpointId
                    || (target.EndpointMonitorId != null && monitors.Contains(target.EndpointMonitorId.Value)))
                .ExecuteDeleteAsync(cancellationToken);
            var orphanedWindowIds = await dbContext.MaintenanceWindows.AsNoTracking()
                .Where(window => maintenanceWindowIds.Contains(window.Id)
                    && !dbContext.MaintenanceTargets.Any(target => target.MaintenanceWindowId == window.Id))
                .Select(window => window.Id)
                .ToArrayAsync(cancellationToken);
            await dbContext.MaintenanceOccurrences
                .Where(occurrence => orphanedWindowIds.Contains(occurrence.MaintenanceWindowId))
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.MaintenanceWindows
                .Where(window => orphanedWindowIds.Contains(window.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await dbContext.AccessGrants
            .Where(grant => grant.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);

        // The permission to contact the host at all dies with the endpoint, so a later endpoint
        // on the same host cannot inherit an authorization nobody granted it.
        await dbContext.TargetAuthorizations
            .Where(authorization => authorization.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.EndpointMonitors
            .Where(monitor => monitor.EndpointId == endpointId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Endpoints
            .Where(endpoint => endpoint.Id == endpointId)
            .ExecuteDeleteAsync(cancellationToken);

        // The origin's robots policy outlives the endpoint only while some other endpoint still
        // sits on that host. Archived endpoints count: one of them can be restored, and it would
        // otherwise come back to a policy that had been deleted underneath it. When nothing is
        // left, keeping the row would mean a future endpoint on the same host silently inheriting
        // a cached policy - including an approved robots exception - that nobody granted it.
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
