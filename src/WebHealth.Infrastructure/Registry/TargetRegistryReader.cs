using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class TargetRegistryReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    ITargetAuthorizationService targetAuthorization,
    IMonitoringEligibilityService monitoringEligibility) : ITargetRegistryReader
{
    public Task<IReadOnlyList<EnvironmentListItem>> ListEnvironmentsAsync(
        Guid websiteId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        ListEnvironmentsAsync(access, environment => environment.WebsiteId == websiteId && environment.DeletedAt == null, cancellationToken);

    public async Task<EnvironmentDetails?> FindEnvironmentAsync(
        Guid environmentId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var canManage = RegistryVisibility.CanManage(access);
        var environment = await visibility.ApplyEnvironmentScope(
                dbContext.Environments.AsNoTracking(), access, DateTimeOffset.UtcNow)
            .Where(candidate => candidate.Id == environmentId)
            .Where(candidate => canManage || candidate.DeletedAt == null)
            .Select(candidate => new EnvironmentRow(
                candidate.Id,
                candidate.WebsiteId,
                candidate.Website.Name,
                candidate.Name,
                candidate.EnvironmentType,
                candidate.IsProduction,
                candidate.BaseUrl,
                candidate.IsActive,
                candidate.DeletedAt != null,
                candidate.Version,
                candidate.Endpoints.Count(endpoint => endpoint.DeletedAt == null && endpoint.IsEnabled)))
            .SingleOrDefaultAsync(cancellationToken);
        if (environment is null)
        {
            return null;
        }

        return new EnvironmentDetails(
            environment.Id,
            environment.WebsiteId,
            environment.WebsiteName,
            environment.Name,
            environment.EnvironmentType,
            environment.IsProduction,
            environment.BaseUrl,
            environment.IsActive,
            environment.IsDeleted,
            environment.Version,
            await ListEndpointsAsync(environment.Id, access, cancellationToken));
    }

    public async Task<IReadOnlyList<EndpointListItem>> ListEndpointsAsync(
        Guid environmentId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var rows = await LoadEndpointRowsAsync(
            visibility.ApplyEndpointScope(dbContext.Endpoints.AsNoTracking(), access, DateTimeOffset.UtcNow)
                .Where(endpoint => endpoint.EnvironmentId == environmentId && endpoint.DeletedAt == null),
            cancellationToken);
        return await ToEndpointItemsAsync(rows, cancellationToken);
    }

    public async Task<EndpointDetails?> FindEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var canManage = RegistryVisibility.CanManage(access);
        var rows = await LoadEndpointRowsAsync(
            visibility.ApplyEndpointScope(dbContext.Endpoints.AsNoTracking(), access, DateTimeOffset.UtcNow)
                .Where(endpoint => endpoint.Id == endpointId)
                .Where(endpoint => canManage || endpoint.DeletedAt == null),
            cancellationToken);
        var endpoint = rows.SingleOrDefault();
        if (endpoint is null)
        {
            return null;
        }

        var ownerNames = await LoadOwnerNamesAsync([endpoint.EffectiveOwnerSubjectId], cancellationToken);
        return new EndpointDetails(
            endpoint.Id,
            endpoint.EnvironmentId,
            endpoint.EnvironmentName,
            endpoint.IsProduction,
            endpoint.WebsiteId,
            endpoint.WebsiteName,
            endpoint.DisplayUrl,
            endpoint.NormalizedUrl,
            endpoint.NormalizationVersion,
            endpoint.OwnerSubjectId,
            ownerNames[endpoint.EffectiveOwnerSubjectId],
            endpoint.OwnerSubjectId is null,
            endpoint.IsEnabled,
            endpoint.IsDeleted,
            endpoint.HttpExceptionReason is not null,
            canManage ? endpoint.HttpExceptionReason : null,
            endpoint.TargetAuthorizationKind is not null,
            canManage ? endpoint.TargetAuthorizationKind : null,
            canManage ? endpoint.TargetAuthorizationEvidence : null,
            canManage ? endpoint.TargetAuthorizationExpiresAt : null,
            endpoint.Version,
            endpoint.MonitorType,
            endpoint.IntervalSeconds,
            endpoint.TimeoutSeconds,
            endpoint.MonitorEnabled,
            await monitoringEligibility.IsEndpointEligibleAsync(endpoint.Id, cancellationToken),
            await targetAuthorization.CanTestEndpointAsync(endpoint.Id, access, cancellationToken));
    }

    public async Task<IReadOnlyList<EnvironmentListItem>> ListDeletedEnvironmentsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return [];
        }

        return await ListEnvironmentsAsync(access, environment => environment.DeletedAt != null, cancellationToken);
    }

    public async Task<IReadOnlyList<EndpointListItem>> ListDeletedEndpointsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return [];
        }

        var rows = await LoadEndpointRowsAsync(
            dbContext.Endpoints.AsNoTracking().Where(endpoint => endpoint.DeletedAt != null),
            cancellationToken);
        return await ToEndpointItemsAsync(rows, cancellationToken);
    }

    private async Task<IReadOnlyList<EnvironmentListItem>> ListEnvironmentsAsync(
        RegistryAccessContext access,
        System.Linq.Expressions.Expression<Func<WebsiteEnvironment, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var rows = await visibility.ApplyEnvironmentScope(
                dbContext.Environments.AsNoTracking(), access, DateTimeOffset.UtcNow)
            .Where(predicate)
            .OrderBy(environment => environment.Website.Name)
            .ThenBy(environment => environment.Name)
            .Select(environment => new EnvironmentRow(
                environment.Id,
                environment.WebsiteId,
                environment.Website.Name,
                environment.Name,
                environment.EnvironmentType,
                environment.IsProduction,
                environment.BaseUrl,
                environment.IsActive,
                environment.DeletedAt != null,
                environment.Version,
                environment.Endpoints.Count(endpoint => endpoint.DeletedAt == null && endpoint.IsEnabled)))
            .ToListAsync(cancellationToken);
        return rows.Select(ToEnvironmentItem).ToArray();
    }

    private async Task<IReadOnlyList<EndpointListItem>> ToEndpointItemsAsync(
        IReadOnlyList<EndpointRow> rows,
        CancellationToken cancellationToken)
    {
        var ownerNames = await LoadOwnerNamesAsync(rows.Select(row => row.EffectiveOwnerSubjectId), cancellationToken);
        return rows.Select(row => new EndpointListItem(
            row.Id,
            row.EnvironmentId,
            row.EnvironmentName,
            row.WebsiteName,
            row.DisplayUrl,
            ownerNames[row.EffectiveOwnerSubjectId],
            row.OwnerSubjectId is null,
            row.IsEnabled,
            row.IsDeleted,
            row.Version,
            row.MonitorType)).ToArray();
    }

    private static Task<List<EndpointRow>> LoadEndpointRowsAsync(
        IQueryable<Endpoint> endpoints,
        CancellationToken cancellationToken) =>
        endpoints.OrderBy(endpoint => endpoint.Environment.Website.Name)
            .ThenBy(endpoint => endpoint.Environment.Name)
            .ThenBy(endpoint => endpoint.DisplayUrl)
            .Select(endpoint => new EndpointRow(
                endpoint.Id,
                endpoint.EnvironmentId,
                endpoint.Environment.Name,
                endpoint.Environment.IsProduction,
                endpoint.Environment.WebsiteId,
                endpoint.Environment.Website.Name,
                endpoint.DisplayUrl,
                endpoint.NormalizedUrl,
                endpoint.NormalizationVersion,
                endpoint.OwnerSubjectId,
                endpoint.OwnerSubjectId ?? endpoint.Environment.Website.OwnerSubjectId,
                endpoint.IsEnabled,
                endpoint.DeletedAt != null,
                endpoint.HttpExceptionReason,
                endpoint.TargetAuthorizations
                    .Where(evidence => evidence.RevokedAt == null
                        && evidence.NormalizedHost == endpoint.NormalizedHost
                        && evidence.Port == endpoint.EffectivePort)
                    .OrderByDescending(evidence => evidence.EffectiveFrom)
                    .Select(evidence => evidence.AuthorizationKind).FirstOrDefault(),
                endpoint.TargetAuthorizations
                    .Where(evidence => evidence.RevokedAt == null
                        && evidence.NormalizedHost == endpoint.NormalizedHost
                        && evidence.Port == endpoint.EffectivePort)
                    .OrderByDescending(evidence => evidence.EffectiveFrom)
                    .Select(evidence => evidence.EvidenceReference).FirstOrDefault(),
                endpoint.TargetAuthorizations
                    .Where(evidence => evidence.RevokedAt == null
                        && evidence.NormalizedHost == endpoint.NormalizedHost
                        && evidence.Port == endpoint.EffectivePort)
                    .OrderByDescending(evidence => evidence.EffectiveFrom)
                    .Select(evidence => evidence.ExpiresAt).FirstOrDefault(),
                endpoint.Version,
                endpoint.Monitors.Select(monitor => monitor.MonitorType).Single(),
                endpoint.Monitors.Select(monitor => monitor.IntervalSeconds).Single(),
                endpoint.Monitors.Select(monitor => monitor.TimeoutSeconds).Single(),
                endpoint.Monitors.Select(monitor => monitor.IsEnabled).Single()))
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<Guid, string>> LoadOwnerNamesAsync(
        IEnumerable<Guid> ownerSubjectIds,
        CancellationToken cancellationToken)
    {
        var ids = ownerSubjectIds.Distinct().ToArray();
        var users = await dbContext.OwnerSubjects.AsNoTracking()
            .Where(owner => ids.Contains(owner.Id) && owner.UserId != null)
            .Join(dbContext.Users.AsNoTracking(), owner => owner.UserId, user => user.Id,
                (owner, user) => new { owner.Id, Name = user.DisplayName })
            .ToListAsync(cancellationToken);
        var teams = await dbContext.OwnerSubjects.AsNoTracking()
            .Where(owner => ids.Contains(owner.Id) && owner.TeamId != null)
            .Join(dbContext.Teams.AsNoTracking(), owner => owner.TeamId, team => team.Id,
                (owner, team) => new { owner.Id, team.Name })
            .ToListAsync(cancellationToken);
        return users.Concat(teams).ToDictionary(owner => owner.Id, owner => owner.Name);
    }

    private static EnvironmentListItem ToEnvironmentItem(EnvironmentRow environment) => new(
        environment.Id,
        environment.WebsiteId,
        environment.WebsiteName,
        environment.Name,
        environment.EnvironmentType,
        environment.IsProduction,
        environment.BaseUrl,
        environment.IsActive,
        environment.IsDeleted,
        environment.Version,
        environment.ActiveEndpointCount);

    private sealed record EnvironmentRow(
        Guid Id, Guid WebsiteId, string WebsiteName, string Name, string EnvironmentType,
        bool IsProduction, string? BaseUrl, bool IsActive, bool IsDeleted, long Version,
        int ActiveEndpointCount);

    private sealed record EndpointRow(
        Guid Id, Guid EnvironmentId, string EnvironmentName, bool IsProduction, Guid WebsiteId, string WebsiteName,
        string DisplayUrl, string NormalizedUrl, short NormalizationVersion, Guid? OwnerSubjectId,
        Guid EffectiveOwnerSubjectId, bool IsEnabled, bool IsDeleted, string? HttpExceptionReason,
        string? TargetAuthorizationKind, string? TargetAuthorizationEvidence,
        DateTimeOffset? TargetAuthorizationExpiresAt, long Version, string MonitorType,
        int IntervalSeconds, int TimeoutSeconds, bool MonitorEnabled);
}
