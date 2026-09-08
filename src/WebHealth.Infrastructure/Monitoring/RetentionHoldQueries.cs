using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class RetentionHoldQueries(ApplicationDbContext database, DateTimeOffset asOf)
{
    private IQueryable<RetentionHold> Active => database.RetentionHolds.Where(hold =>
        hold.CreatedAt <= asOf && hold.ReleasedAt == null && (hold.ExpiresAt == null || hold.ExpiresAt > asOf));

    public IQueryable<Guid> EndpointIds() => database.Endpoints.Where(endpoint => Active.Any(hold =>
        (hold.ScopeType == "Endpoint" && hold.ScopeId == endpoint.Id)
        || (hold.ScopeType == "Environment" && hold.ScopeId == endpoint.EnvironmentId)
        || (hold.ScopeType == "Website" && hold.ScopeId == endpoint.Environment.WebsiteId)
        || (hold.ScopeType == "Client" && hold.ScopeId == endpoint.Environment.Website.ClientId)))
        .Select(endpoint => endpoint.Id);

    public IQueryable<Guid> MonitorIds()
    {
        var endpoints = EndpointIds();
        return database.EndpointMonitors.Where(monitor => endpoints.Contains(monitor.EndpointId)
            || Active.Any(hold => hold.ScopeType == "Monitor" && hold.ScopeId == monitor.Id))
            .Select(monitor => monitor.Id);
    }

    public IQueryable<Guid> IncidentIds()
    {
        var monitors = MonitorIds();
        return database.Incidents.Where(incident => monitors.Contains(incident.EndpointMonitorId)
            || Active.Any(hold => hold.ScopeType == "Incident" && hold.ScopeId == incident.Id)
            || database.IncidentEvidence.Any(evidence => evidence.IncidentId == incident.Id
                && Active.Any(hold => (hold.ScopeType == "LogicalCheck" && hold.ScopeId == evidence.LogicalCheckId)
                    || (hold.ScopeType == "PageAuditRun" && hold.ScopeId == evidence.PageAuditRunId))))
            .Select(incident => incident.Id);
    }

    public IQueryable<Guid> LogicalCheckIds()
    {
        var monitors = MonitorIds();
        var incidents = IncidentIds();
        return database.LogicalChecks.Where(check => monitors.Contains(check.EndpointMonitorId)
            || Active.Any(hold => hold.ScopeType == "LogicalCheck" && hold.ScopeId == check.Id)
            || database.IncidentEvidence.Any(evidence => evidence.LogicalCheckId == check.Id && incidents.Contains(evidence.IncidentId)))
            .Select(check => check.Id);
    }

    public IQueryable<Guid> CrawlRunIds()
    {
        var endpoints = EndpointIds();
        return database.CrawlRuns.Where(run => endpoints.Contains(run.EndpointId)
            || Active.Any(hold => hold.ScopeType == "CrawlRun" && hold.ScopeId == run.Id)).Select(run => run.Id);
    }

    public IQueryable<Guid> PageAuditRunIds()
    {
        var endpoints = EndpointIds();
        var incidents = IncidentIds();
        return database.PageAuditRuns.Where(run => endpoints.Contains(run.EndpointId)
            || Active.Any(hold => hold.ScopeType == "PageAuditRun" && hold.ScopeId == run.Id)
            || database.IncidentEvidence.Any(evidence => evidence.PageAuditRunId == run.Id && incidents.Contains(evidence.IncidentId)))
            .Select(run => run.Id);
    }

    public IQueryable<Guid> RelatedEndpointIds()
    {
        var monitors = MonitorIds();
        var checks = LogicalCheckIds();
        var incidents = IncidentIds();
        var crawls = CrawlRunIds();
        var audits = PageAuditRunIds();
        return EndpointIds()
            .Union(database.EndpointMonitors.Where(item => monitors.Contains(item.Id)).Select(item => item.EndpointId))
            .Union(database.LogicalChecks.Where(item => checks.Contains(item.Id)).Select(item => item.EndpointMonitor.EndpointId))
            .Union(database.Incidents.Where(item => incidents.Contains(item.Id)).Select(item => item.EndpointMonitor.EndpointId))
            .Union(database.CrawlRuns.Where(item => crawls.Contains(item.Id)).Select(item => item.EndpointId))
            .Union(database.PageAuditRuns.Where(item => audits.Contains(item.Id)).Select(item => item.EndpointId));
    }
}
