using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Archiving;
using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.PageAudits;
using WebHealth.Domain.PngAudits;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Archiving;

internal sealed class RunHistoryArchive(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    TimeProvider timeProvider) : IRunHistoryArchive
{
    public async Task<RunHistoryArchiveResult> ArchiveFinishedAsync(
        RunHistoryArea area,
        RunHistoryScope scope,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(scope);
        if (!CanArchive(access))
        {
            return RunHistoryArchiveResult.Failure(
                RunHistoryArchiveStatus.Forbidden, "You cannot archive run history.");
        }

        var endpointId = scope.EndpointId;
        if (!await IsEndpointVisibleAsync(endpointId, access, cancellationToken))
        {
            return RunHistoryArchiveResult.Failure(
                RunHistoryArchiveStatus.NotFound, "The endpoint is not available.");
        }

        var now = timeProvider.GetUtcNow();
        var archived = area switch
        {
            RunHistoryArea.Check => await dbContext.LogicalChecks
                .Where(check => check.EndpointMonitor.EndpointId == endpointId
                    && check.ArchivedAt == null
                    && check.State == LogicalCheckStates.Completed)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(check => check.ArchivedAt, now),
                    cancellationToken),
            RunHistoryArea.Crawl => await dbContext.CrawlRuns
                .Where(run => run.EndpointId == endpointId
                    && run.ArchivedAt == null
                    && run.Status != CrawlRunStatuses.Running)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(run => run.ArchivedAt, now),
                    cancellationToken),
            RunHistoryArea.PageAudit => await dbContext.PageAuditRuns
                .Where(run => run.EndpointId == endpointId
                    && run.ArchivedAt == null
                    && run.Status != PageAuditRunStatuses.Queued
                    && run.Status != PageAuditRunStatuses.Running
                    && (scope.Category == null || run.Category == scope.Category)
                    && (scope.Strategy == null || run.Strategy == scope.Strategy))
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(run => run.ArchivedAt, now),
                    cancellationToken),
            RunHistoryArea.PngAudit => await dbContext.PngAuditRuns
                .Where(run => run.EndpointId == endpointId
                    && run.ArchivedAt == null
                    && run.Status != PngAuditRunStatuses.Queued
                    && run.Status != PngAuditRunStatuses.Running)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(run => run.ArchivedAt, now),
                    cancellationToken),
            _ => 0
        };

        return RunHistoryArchiveResult.Success(archived);
    }

    public async Task<RunHistoryArchiveResult> RestoreAsync(
        RunHistoryArea area,
        Guid recordId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!CanArchive(access))
        {
            return RunHistoryArchiveResult.Failure(
                RunHistoryArchiveStatus.Forbidden, "You cannot restore run history.");
        }

        var endpointId = await FindEndpointIdAsync(area, recordId, cancellationToken);
        if (endpointId is not { } owner
            || !await IsEndpointVisibleAsync(owner, access, cancellationToken))
        {
            return RunHistoryArchiveResult.Failure(
                RunHistoryArchiveStatus.NotFound, "The record is not available.");
        }

        var restored = area switch
        {
            RunHistoryArea.Check => await dbContext.LogicalChecks
                .Where(check => check.Id == recordId && check.ArchivedAt != null)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(check => check.ArchivedAt, (DateTimeOffset?)null),
                    cancellationToken),
            RunHistoryArea.Crawl => await dbContext.CrawlRuns
                .Where(run => run.Id == recordId && run.ArchivedAt != null)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(run => run.ArchivedAt, (DateTimeOffset?)null),
                    cancellationToken),
            RunHistoryArea.PageAudit => await dbContext.PageAuditRuns
                .Where(run => run.Id == recordId && run.ArchivedAt != null)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(run => run.ArchivedAt, (DateTimeOffset?)null),
                    cancellationToken),
            RunHistoryArea.PngAudit => await dbContext.PngAuditRuns
                .Where(run => run.Id == recordId && run.ArchivedAt != null)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(run => run.ArchivedAt, (DateTimeOffset?)null),
                    cancellationToken),
            _ => 0
        };

        return restored == 0
            ? RunHistoryArchiveResult.Failure(
                RunHistoryArchiveStatus.NotArchived, "The record is not archived.")
            : RunHistoryArchiveResult.Success(restored);
    }

    private Task<Guid?> FindEndpointIdAsync(
        RunHistoryArea area,
        Guid recordId,
        CancellationToken cancellationToken) => area switch
        {
            RunHistoryArea.Check => dbContext.LogicalChecks.AsNoTracking()
                .Where(check => check.Id == recordId)
                .Select(check => (Guid?)check.EndpointMonitor.EndpointId)
                .SingleOrDefaultAsync(cancellationToken),
            RunHistoryArea.Crawl => dbContext.CrawlRuns.AsNoTracking()
                .Where(run => run.Id == recordId)
                .Select(run => (Guid?)run.EndpointId)
                .SingleOrDefaultAsync(cancellationToken),
            RunHistoryArea.PageAudit => dbContext.PageAuditRuns.AsNoTracking()
                .Where(run => run.Id == recordId)
                .Select(run => (Guid?)run.EndpointId)
                .SingleOrDefaultAsync(cancellationToken),
            RunHistoryArea.PngAudit => dbContext.PngAuditRuns.AsNoTracking()
                .Where(run => run.Id == recordId)
                .Select(run => (Guid?)run.EndpointId)
                .SingleOrDefaultAsync(cancellationToken),
            _ => Task.FromResult<Guid?>(null)
        };

    private Task<bool> IsEndpointVisibleAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken) =>
        visibility.ApplyEndpointScope(
                dbContext.Endpoints.AsNoTracking().Where(endpoint => endpoint.DeletedAt == null),
                access,
                timeProvider.GetUtcNow())
            .AnyAsync(endpoint => endpoint.Id == endpointId, cancellationToken);

    private static bool CanArchive(RegistryAccessContext access) =>
        access.Roles.Contains(ApplicationRoles.Administrator)
        || access.Roles.Contains(ApplicationRoles.Operations);
}
