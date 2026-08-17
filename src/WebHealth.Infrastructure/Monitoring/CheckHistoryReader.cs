using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class CheckHistoryReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility) : ICheckHistoryReader
{
    private const int PageSize = 25;

    public async Task<CheckHistoryPage?> ListForEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var endpoint = await visibility.ApplyEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now)
            .Where(candidate => candidate.Id == endpointId)
            .Select(candidate => new { candidate.Id, candidate.NormalizedUrl })
            .SingleOrDefaultAsync(cancellationToken);
        if (endpoint is null)
        {
            return null;
        }

        var boundedPage = Math.Max(page, 1);
        var query = dbContext.LogicalChecks.AsNoTracking()
            .Where(check => check.EndpointMonitor.EndpointId == endpointId)
            .OrderByDescending(check => check.CreatedAt);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((boundedPage - 1) * PageSize)
            .Take(PageSize)
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
                check.Result != null && check.Result.CountsForUptime))
            .ToArrayAsync(cancellationToken);

        return new(endpoint.Id, endpoint.NormalizedUrl, items, boundedPage, PageSize, totalCount);
    }

    public async Task<CheckDetails?> FindCheckAsync(
        Guid logicalCheckId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var check = await dbContext.LogicalChecks.AsNoTracking()
            .Include(candidate => candidate.EndpointMonitor).ThenInclude(monitor => monitor.Endpoint)
            .Include(candidate => candidate.InitiatedByUser)
            .Include(candidate => candidate.Result).ThenInclude(result => result!.Findings)
            .Include(candidate => candidate.Result).ThenInclude(result => result!.RedirectHops)
            .SingleOrDefaultAsync(candidate => candidate.Id == logicalCheckId, cancellationToken);
        if (check is null)
        {
            return null;
        }

        var endpointId = check.EndpointMonitor.EndpointId;
        var now = DateTimeOffset.UtcNow;
        var visible = await visibility.ApplyEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now)
            .AnyAsync(candidate => candidate.Id == endpointId, cancellationToken);
        if (!visible)
        {
            return null;
        }

        var result = check.Result;
        return new CheckDetails(
            check.Id,
            endpointId,
            check.EndpointMonitor.Endpoint.NormalizedUrl,
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
            result?.DecodedLength,
            result?.ResponseTruncated ?? false,
            result?.SafeDiagnostic,
            result?.CountsForUptime ?? false,
            result?.Findings.Select(finding => new CheckFindingItem(
                finding.RuleKey, finding.Severity, finding.ObservedValue, finding.ExpectedValue, finding.IssueKey))
                .ToArray() ?? [],
            result?.RedirectHops.OrderBy(hop => hop.HopNumber).Select(hop => new CheckRedirectHopItem(
                hop.HopNumber, hop.NormalizedFromUrl, hop.NormalizedToUrl, hop.HttpStatus, hop.IsLoop))
                .ToArray() ?? []);
    }
}
