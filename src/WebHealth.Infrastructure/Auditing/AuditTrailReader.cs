using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Auditing;

public sealed class AuditTrailReader(ApplicationDbContext dbContext) : IAuditTrailReader
{
    private const int MaximumPageSize = 100;

    public async Task<AuditSearchResult> SearchAsync(
        AuditSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var requestedPage = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var events = ApplyFilters(dbContext.AuditEvents.AsNoTracking(), query);
        var totalCount = await events.CountAsync(cancellationToken);
        var lastPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Min(requestedPage, lastPage);
        var rows = await events
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .ThenByDescending(auditEvent => auditEvent.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .GroupJoin(
                dbContext.Users.AsNoTracking(),
                auditEvent => auditEvent.ActorUserId,
                user => user.Id,
                (auditEvent, users) => new { AuditEvent = auditEvent, Users = users })
            .SelectMany(
                row => row.Users.DefaultIfEmpty(),
                (row, user) => new
                {
                    row.AuditEvent,
                    ActorDisplayName = user == null ? row.AuditEvent.ActorIdentifier : user.DisplayName
                })
            .ToListAsync(cancellationToken);

        return new AuditSearchResult(
            rows.Select(row => new AuditEventSummary(
                row.AuditEvent.Id,
                row.AuditEvent.OccurredAt,
                row.AuditEvent.ActorUserId,
                row.ActorDisplayName,
                row.AuditEvent.Action,
                row.AuditEvent.EntityType,
                row.AuditEvent.EntityIdentifier,
                row.AuditEvent.Outcome,
                DeserializeValues(row.AuditEvent.BeforeValues),
                DeserializeValues(row.AuditEvent.AfterValues))).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<IReadOnlyList<AuditActor>> ListActorsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Users.AsNoTracking()
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .Select(user => new AuditActor(user.Id, user.DisplayName, user.Email ?? string.Empty))
            .ToListAsync(cancellationToken);

    private static IQueryable<AuditEvent> ApplyFilters(
        IQueryable<AuditEvent> events,
        AuditSearchQuery query)
    {
        if (query.FromDate is { } fromDate)
        {
            var from = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            events = events.Where(auditEvent => auditEvent.OccurredAt >= from);
        }

        if (query.ToDate is { } toDate && toDate < DateOnly.MaxValue)
        {
            var until = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            events = events.Where(auditEvent => auditEvent.OccurredAt < until);
        }

        if (query.ActorUserId is { } actorUserId)
        {
            events = events.Where(auditEvent => auditEvent.ActorUserId == actorUserId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            events = events.Where(auditEvent => EF.Functions.ILike(auditEvent.Action, $"%{action}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Entity))
        {
            var entity = query.Entity.Trim();
            events = events.Where(auditEvent =>
                EF.Functions.ILike(auditEvent.EntityType, $"%{entity}%")
                || EF.Functions.ILike(auditEvent.EntityIdentifier, $"%{entity}%"));
        }

        return events;
    }

    private static IReadOnlyDictionary<string, string?> DeserializeValues(string? values)
    {
        if (string.IsNullOrWhiteSpace(values))
        {
            return new Dictionary<string, string?>();
        }

        using var document = JsonDocument.Parse(values);
        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => property.Value.GetString(),
                _ => property.Value.GetRawText()
            });
    }
}
