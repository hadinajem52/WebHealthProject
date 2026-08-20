using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Domain.Incidents;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class CheckHistoryReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    IncidentVisibility incidentVisibility,
    TimeProvider timeProvider) : ICheckHistoryReader
{
    private const int PageSize = 25;

    public async Task<CheckHistoryItem?> FindLatestForEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var canManage = RegistryVisibility.CanManage(access);
        var isVisible = await visibility.ApplyEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now)
            .Where(endpoint => canManage || endpoint.DeletedAt == null)
            .AnyAsync(endpoint => endpoint.Id == endpointId, cancellationToken);
        if (!isVisible)
        {
            return null;
        }

        return await dbContext.LogicalChecks.AsNoTracking()
            .Where(check => check.EndpointMonitor.EndpointId == endpointId)
            .OrderByDescending(check => check.CreatedAt)
            .ThenByDescending(check => check.Id)
            .Select(check => new CheckHistoryItem(
                check.Id,
                check.Source,
                check.State,
                check.ScheduledFor,
                check.RequestedAt,
                check.InitiatedByUser == null ? null : check.InitiatedByUser.DisplayName,
                check.CompletedAt,
                check.Result == null ? null : check.Result.Outcome,
                check.Result == null ? null : check.Result.FailureCategory,
                check.Result == null ? null : check.Result.HttpStatus,
                check.Result == null ? null : check.Result.TotalDurationMs,
                check.Result == null ? null : check.Result.MonitorSource,
                check.Result != null && check.Result.CountsForUptime))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CheckHistoryPage?> ListForEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var canManage = RegistryVisibility.CanManage(access);
        var endpoint = await visibility.ApplyEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now)
            .Where(candidate => canManage || candidate.DeletedAt == null)
            .Where(candidate => candidate.Id == endpointId)
            .Select(candidate => new { candidate.Id, candidate.DisplayUrl })
            .SingleOrDefaultAsync(cancellationToken);
        if (endpoint is null)
        {
            return null;
        }

        var checksForEndpoint = dbContext.LogicalChecks.AsNoTracking()
            .Where(check => check.EndpointMonitor.EndpointId == endpointId);
        var totalCount = await checksForEndpoint.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        var boundedPage = Math.Clamp(page, 1, totalPages);
        var items = await checksForEndpoint
            .OrderByDescending(check => check.CreatedAt)
            .ThenByDescending(check => check.Id)
            .Skip((boundedPage - 1) * PageSize)
            .Take(PageSize)
            .Select(check => new
            {
                Item = new CheckHistoryItem(
                    check.Id,
                    check.Source,
                    check.State,
                    check.ScheduledFor,
                    check.RequestedAt,
                    check.InitiatedByUser == null ? null : check.InitiatedByUser.DisplayName,
                    check.CompletedAt,
                    check.Result == null ? null : check.Result.Outcome,
                    check.Result == null ? null : check.Result.FailureCategory,
                    check.Result == null ? null : check.Result.HttpStatus,
                    check.Result == null ? null : check.Result.TotalDurationMs,
                    check.Result == null ? null : check.Result.MonitorSource,
                    check.Result != null && check.Result.CountsForUptime),
                ConfigurationFingerprint = check.ConfigurationSnapshot.ConfigurationFingerprint
            })
            .ToArrayAsync(cancellationToken);

        // BR-P05: only completed results carry a measurement context, so a still-running check
        // neither claims comparability nor breaks it.
        var comparability = PerformanceComparability.Evaluate(items
            .Where(row => row.Item.MonitorSource is not null)
            .Select(row => new PerformanceSampleContext(
                row.Item.MonitorSource!, row.ConfigurationFingerprint)));

        var historyItems = items.Select(row => row.Item).ToArray();
        var knownIncidents = await LoadKnownIncidentsAsync(
            historyItems.Select(item => item.LogicalCheckId), access, cancellationToken);

        return new(
            endpoint.Id,
            endpoint.DisplayUrl,
            historyItems.Select(item => item with
            {
                KnownIncidents = knownIncidents.GetValueOrDefault(item.LogicalCheckId, [])
            }).ToArray(),
            boundedPage,
            PageSize,
            totalCount,
            items.FirstOrDefault()?.ConfigurationFingerprint,
            comparability);
    }

    public async Task<CheckDetails?> FindCheckAsync(
        Guid logicalCheckId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var endpointId = await dbContext.LogicalChecks.AsNoTracking()
            .Where(candidate => candidate.Id == logicalCheckId)
            .Select(candidate => (Guid?)candidate.EndpointMonitor.EndpointId)
            .SingleOrDefaultAsync(cancellationToken);
        if (endpointId is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var canManage = RegistryVisibility.CanManage(access);
        var visible = await visibility.ApplyEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now)
            .Where(candidate => canManage || candidate.DeletedAt == null)
            .AnyAsync(candidate => candidate.Id == endpointId, cancellationToken);
        if (!visible)
        {
            return null;
        }

        var check = await dbContext.LogicalChecks.AsNoTracking()
            .Include(candidate => candidate.EndpointMonitor).ThenInclude(monitor => monitor.Endpoint)
            .Include(candidate => candidate.InitiatedByUser)
            .Include(candidate => candidate.Result).ThenInclude(result => result!.Findings)
            .Include(candidate => candidate.Result).ThenInclude(result => result!.RedirectHops)
            .SingleAsync(candidate => candidate.Id == logicalCheckId, cancellationToken);

        var result = check.Result;
        var details = new CheckDetails(
            check.Id,
            endpointId.Value,
            check.EndpointMonitor.Endpoint.DisplayUrl,
            check.Source,
            check.State,
            check.ScheduledFor,
            check.RequestedAt,
            check.InitiatedByUser?.DisplayName,
            check.CreatedAt,
            check.StartedAt,
            check.CompletedAt,
            result?.Outcome,
            result?.FailureCategory,
            result?.HttpStatus,
            result?.TotalDurationMs,
            result?.DnsDurationMs,
            result?.ConnectDurationMs,
            result?.TlsDurationMs,
            result?.TtfbDurationMs,
            result?.TransferredLength,
            result?.DecodedLength,
            result?.LengthSource,
            result?.MonitorSource,
            result?.MeasuredAt,
            result?.ResponseTruncated ?? false,
            result?.SafeDiagnostic,
            result?.CountsForUptime ?? false,
            result?.Findings.Select(finding => new CheckFindingItem(
                finding.RuleKey, finding.Severity, finding.ObservedValue, finding.ExpectedValue, finding.IssueKey))
                .ToArray() ?? [],
            result?.RedirectHops.OrderBy(hop => hop.HopNumber).Select(hop => new CheckRedirectHopItem(
                hop.HopNumber, hop.NormalizedFromUrl, hop.NormalizedToUrl, hop.HttpStatus, hop.IsLoop))
                .ToArray() ?? []);

        var knownIncidents = await LoadKnownIncidentsAsync([logicalCheckId], access, cancellationToken);
        return details with
        {
            KnownIncidents = knownIncidents.GetValueOrDefault(logicalCheckId, [])
        };
    }

    private async Task<Dictionary<Guid, IReadOnlyList<KnownIncidentItem>>> LoadKnownIncidentsAsync(
        IEnumerable<Guid> logicalCheckIds,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        var checkIds = logicalCheckIds.Distinct().ToArray();
        if (checkIds.Length == 0)
        {
            return [];
        }

        var visibleIncidentIds = incidentVisibility
            .Apply(dbContext.Incidents.AsNoTracking(), access, timeProvider.GetUtcNow())
            .Select(incident => incident.Id);
        var rows = await dbContext.IncidentEvidence.AsNoTracking()
            .Where(evidence => evidence.LogicalCheckId != null
                && checkIds.Contains(evidence.LogicalCheckId.Value)
                && visibleIncidentIds.Contains(evidence.IncidentId)
                && evidence.Incident.AcknowledgedAt != null
                && IncidentStatuses.Active.Contains(evidence.Incident.Status))
            .Select(evidence => new
            {
                LogicalCheckId = evidence.LogicalCheckId!.Value,
                evidence.IncidentId,
                evidence.Incident.IssueKey,
                evidence.Incident.Status,
                AcknowledgedAt = evidence.Incident.AcknowledgedAt!.Value
            })
            .Distinct()
            .OrderByDescending(row => row.AcknowledgedAt)
            .ThenBy(row => row.IncidentId)
            .ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(row => row.LogicalCheckId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<KnownIncidentItem>)group
                    .Select(row => new KnownIncidentItem(
                        row.IncidentId, row.IssueKey, row.Status, row.AcknowledgedAt))
                    .ToArray());
    }
}
