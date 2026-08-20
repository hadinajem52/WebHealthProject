using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Assignments;
using WebHealth.Application.Incidents;
using WebHealth.Application.Registry;
using WebHealth.Domain.Incidents;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Incidents;

internal sealed class IncidentReader(
    ApplicationDbContext dbContext,
    IncidentVisibility incidentVisibility,
    IAssignmentAccessEvaluator assignmentAccess,
    TimeProvider timeProvider) : IIncidentReader
{
    private const int PageSize = 25;

    public async Task<IncidentListPage> ListAsync(
        IncidentListFilter filter,
        RegistryAccessContext access,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var query = incidentVisibility.Apply(dbContext.Incidents.AsNoTracking(), access, now);
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(incident => incident.Status == filter.Status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Severity))
        {
            query = query.Where(incident => incident.Severity == filter.Severity);
        }

        if (filter.UnacknowledgedOnly)
        {
            query = query.Where(incident =>
                incident.AcknowledgedAt == null && IncidentStatuses.Active.Contains(incident.Status));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        var boundedPage = Math.Clamp(page, 1, totalPages);
        var rows = await query
            .OrderByDescending(incident => incident.OpenedAt)
            .ThenByDescending(incident => incident.Id)
            .Skip((boundedPage - 1) * PageSize)
            .Take(PageSize)
            .Select(incident => new
            {
                incident.Id,
                EndpointDisplayUrl = incident.EndpointMonitor.Endpoint.DisplayUrl,
                ClientName = incident.EndpointMonitor.Endpoint.Environment.Website.Client.Name,
                WebsiteName = incident.EndpointMonitor.Endpoint.Environment.Website.Name,
                EnvironmentName = incident.EndpointMonitor.Endpoint.Environment.Name,
                incident.IssueKey,
                incident.Severity,
                incident.Status,
                incident.OpenedAt,
                incident.AcknowledgedAt,
                incident.OwnerSubjectId,
                incident.RecurrenceCount
            })
            .ToArrayAsync(cancellationToken);

        var ownerNames = await ResolveOwnerNamesAsync(rows.Select(row => row.OwnerSubjectId), cancellationToken);
        var items = rows.Select(row => new IncidentListItem(
            row.Id, row.EndpointDisplayUrl, row.ClientName, row.WebsiteName, row.EnvironmentName,
            row.IssueKey, row.Severity, row.Status, row.OpenedAt, row.AcknowledgedAt,
            ownerNames.GetValueOrDefault(row.OwnerSubjectId, "Unassigned"), row.RecurrenceCount)).ToArray();
        return new(items, boundedPage, PageSize, totalCount);
    }

    public async Task<IncidentDetails?> FindAsync(
        Guid incidentId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var visible = await incidentVisibility.Apply(dbContext.Incidents.AsNoTracking(), access, now)
            .AnyAsync(incident => incident.Id == incidentId, cancellationToken);
        if (!visible)
        {
            return null;
        }

        var incident = await dbContext.Incidents.AsNoTracking()
            .Include(candidate => candidate.EndpointMonitor).ThenInclude(monitor => monitor.Endpoint)
                .ThenInclude(endpoint => endpoint.Environment).ThenInclude(environment => environment.Website)
                    .ThenInclude(website => website.Client)
            .Include(candidate => candidate.Events).ThenInclude(incidentEvent => incidentEvent.ActorUser)
            .Include(candidate => candidate.Evidence)
            .AsSplitQuery()
            .SingleAsync(candidate => candidate.Id == incidentId, cancellationToken);

        var notificationEvents = await dbContext.NotificationEvents.AsNoTracking()
            .Include(notificationEvent => notificationEvent.Deliveries)
            .Where(notificationEvent => notificationEvent.IncidentId == incidentId)
            .OrderBy(notificationEvent => notificationEvent.OccurredAt)
            .ToArrayAsync(cancellationToken);

        var ownerIds = incident.Events
            .SelectMany(incidentEvent => new[] { incidentEvent.FromOwnerSubjectId, incidentEvent.ToOwnerSubjectId })
            .Where(id => id is not null).Select(id => id!.Value)
            .Append(incident.OwnerSubjectId)
            .Distinct();
        var ownerNames = await ResolveOwnerNamesAsync(ownerIds, cancellationToken);

        var timeline = incident.Events.OrderBy(incidentEvent => incidentEvent.SequenceNumber)
            .Select(incidentEvent => new IncidentTimelineEntry(
                incidentEvent.Id,
                incidentEvent.SequenceNumber,
                incidentEvent.EventType,
                incidentEvent.FromStatus,
                incidentEvent.ToStatus,
                incidentEvent.FromOwnerSubjectId is { } fromOwner ? ownerNames.GetValueOrDefault(fromOwner, "Unknown owner") : null,
                incidentEvent.ToOwnerSubjectId is { } toOwner ? ownerNames.GetValueOrDefault(toOwner, "Unknown owner") : null,
                incidentEvent.BoundedNote,
                incidentEvent.ActorUser?.DisplayName,
                incidentEvent.OccurredAt))
            .ToArray();

        var evidence = incident.Evidence.OrderBy(item => item.CapturedAt)
            .Select(item => new IncidentEvidenceItem(item.Id, item.EvidenceType, item.EvidenceRole, item.CapturedAt))
            .ToArray();

        var notifications = notificationEvents.Select(notificationEvent => new IncidentNotificationItem(
            notificationEvent.Id,
            notificationEvent.EventType,
            notificationEvent.IsSuppressed,
            notificationEvent.SuppressionReason,
            notificationEvent.OccurredAt,
            notificationEvent.Deliveries.Select(delivery => new IncidentNotificationDeliveryItem(
                delivery.NormalizedRecipient, delivery.State, delivery.AttemptCount, delivery.SentAt)).ToArray()))
            .ToArray();

        // Mirrors IncidentLifecycleService.CanManageAsync exactly — that method is the actual
        // authorization authority for every mutation; this copy only drives what the UI shows.
        var canManage = RegistryVisibility.CanManage(access)
            || (access.Roles.Contains(ApplicationRoles.DeveloperSupport)
                && await assignmentAccess.IsAssignedAsync(access.UserId, incident.OwnerSubjectId, now, cancellationToken));

        return new IncidentDetails(
            incident.Id,
            incident.EndpointMonitorId,
            incident.EndpointMonitor.Endpoint.DisplayUrl,
            incident.EndpointMonitor.Endpoint.Environment.Website.Client.Name,
            incident.EndpointMonitor.Endpoint.Environment.Website.Name,
            incident.EndpointMonitor.Endpoint.Environment.Name,
            incident.IssueKey,
            incident.Severity,
            incident.Status,
            incident.RecurrenceCount,
            incident.PreviousIncidentId,
            incident.OpenedAt,
            incident.AcknowledgedAt,
            incident.RecoveryStartedAt,
            incident.ResolvedAt,
            incident.ClosedAt,
            incident.RecoveryDurationMs,
            incident.OutageDurationMs,
            incident.ResolutionCategory,
            incident.ResolutionNote,
            incident.OwnerSubjectId,
            ownerNames.GetValueOrDefault(incident.OwnerSubjectId, "Unassigned"),
            incident.Version,
            canManage,
            timeline,
            evidence,
            notifications);
    }

    private async Task<Dictionary<Guid, string>> ResolveOwnerNamesAsync(
        IEnumerable<Guid> ownerSubjectIds, CancellationToken cancellationToken)
    {
        var ids = ownerSubjectIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var subjects = await dbContext.OwnerSubjects.AsNoTracking()
            .Where(subject => ids.Contains(subject.Id))
            .Select(subject => new { subject.Id, subject.UserId, subject.TeamId })
            .ToArrayAsync(cancellationToken);
        var userIds = subjects.Where(subject => subject.UserId != null).Select(subject => subject.UserId!.Value).ToArray();
        var teamIds = subjects.Where(subject => subject.TeamId != null).Select(subject => subject.TeamId!.Value).ToArray();
        var userNames = await dbContext.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        var teamNames = await dbContext.Teams.AsNoTracking()
            .Where(team => teamIds.Contains(team.Id))
            .ToDictionaryAsync(team => team.Id, team => team.Name, cancellationToken);
        return subjects.ToDictionary(
            subject => subject.Id,
            subject => subject.UserId is { } userId
                ? userNames.GetValueOrDefault(userId, "Unknown user")
                : subject.TeamId is { } teamId
                    ? teamNames.GetValueOrDefault(teamId, "Unknown team")
                    : "Unassigned");
    }
}
