using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Health;
using WebHealth.Application.Incidents;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Incidents;
using WebHealth.Domain.Notifications;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Notifications;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Incidents;

internal sealed class IncidentAutomationService(
    ApplicationDbContext dbContext,
    IAuditTrailWriter auditTrail,
    NotificationEventWriter notificationEventWriter)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task ApplyAsync(
        LogicalCheck check,
        NormalizedCheckResult result,
        HealthConfirmationDecision healthDecision,
        HealthCounterMode counterMode,
        bool isMaintenance,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? observedCertificateFingerprint = null)
    {
        if (counterMode != HealthCounterMode.Count)
        {
            return;
        }

        var incidents = await LoadActiveAsync(check.EndpointMonitorId, cancellationToken);
        if (observedCertificateFingerprint is not null)
        {
            await ApplyCertificateRenewalAsync(
                check, result, incidents, observedCertificateFingerprint, isMaintenance, now, cancellationToken);
        }

        if (result.Outcome == HttpResultOutcomes.Healthy)
        {
            await ApplyRecoveryAsync(check, result, healthDecision, incidents, isMaintenance, now, cancellationToken);
            return;
        }

        var interruptedIncidentIds = await InterruptRecoveryAsync(
            check, result, incidents, now, cancellationToken);
        await ApplyFailuresAsync(
            check, result, healthDecision, incidents, interruptedIncidentIds, isMaintenance, now, cancellationToken);
    }

    private async Task ApplyFailuresAsync(
        LogicalCheck check,
        NormalizedCheckResult result,
        HealthConfirmationDecision healthDecision,
        List<Incident> incidents,
        IReadOnlySet<Guid> interruptedIncidentIds,
        bool isMaintenance,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The engine already applied each issue's own confirmation count (BR-P03), so the
        // threshold lives in exactly one place.
        foreach (var issueKey in healthDecision.ConfirmedIssueKeys)
        {
            var severity = SelectSeverity(result, issueKey);
            var incident = incidents.SingleOrDefault(candidate => candidate.IssueKey == issueKey);
            if (incident is null)
            {
                incident = await OpenAsync(
                    check, result, issueKey, severity, isMaintenance, now, cancellationToken);
                incidents.Add(incident);
                continue;
            }

            if (interruptedIncidentIds.Contains(incident.Id))
            {
                continue;
            }

            await RecordEvidenceMutationAsync(
                incident,
                check,
                result,
                IncidentEvidenceTypes.Failure,
                "ConfirmedFailure",
                IncidentAuditAction.FailureRecorded,
                now,
                cancellationToken,
                severity);
        }
    }

    /// <summary>
    /// BR-C06. A renewed certificate has a new fingerprint and therefore a new expiry issue key,
    /// which leaves any incident opened for the previous certificate with nothing left to
    /// observe. Resolving it here rather than waiting for a healthy result matters: a
    /// certificate renewed into another warning band never produces a healthy result, and the
    /// stale incident would stay open against a certificate that no longer exists.
    /// </summary>
    private async Task ApplyCertificateRenewalAsync(
        LogicalCheck check,
        NormalizedCheckResult result,
        List<Incident> incidents,
        string observedFingerprint,
        bool isMaintenance,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var superseded = incidents
            .Where(incident => SslMonitorIdentity.IsSupersededExpiryIssueKey(
                incident.IssueKey, observedFingerprint))
            .ToArray();
        foreach (var incident in superseded)
        {
            AddEvent(
                incident,
                IncidentEventTypes.CertificateRenewed,
                now,
                note: $"A different certificate is now presented (SHA-256 {observedFingerprint}).");
            await ResolveAsync(
                incident,
                check,
                result,
                isMaintenance,
                now,
                cancellationToken,
                IncidentResolutionCategories.CertificateRenewed,
                "The certificate this incident tracked was replaced by a different certificate.");
            incidents.Remove(incident);
        }
    }

    /// <summary>
    /// An incident is as severe as the finding that confirmed it (BR-C04). A confirmed issue
    /// with no finding behind it came from a transport failure, which has no severity of its
    /// own and is always critical.
    /// </summary>
    private static string SelectSeverity(NormalizedCheckResult result, string issueKey) =>
        result.Findings
            .Where(finding => string.Equals(finding.IssueKey, issueKey, StringComparison.Ordinal))
            .Select(finding => finding.Severity)
            .DefaultIfEmpty(IncidentSeverities.Critical)
            .Aggregate(FindingSeverities.Max);

    private async Task ApplyRecoveryAsync(
        LogicalCheck check,
        NormalizedCheckResult result,
        HealthConfirmationDecision healthDecision,
        IReadOnlyCollection<Incident> incidents,
        bool isMaintenance,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (healthDecision.Transition == HealthTransition.RecoveryStarted)
        {
            foreach (var incident in incidents.Where(candidate =>
                         candidate.Status != IncidentStatuses.MonitoringRecovery))
            {
                await BeginRecoveryAsync(incident, check, result, now, cancellationToken);
            }
        }
        else if (healthDecision.Transition == HealthTransition.RecoveryConfirmed)
        {
            // incidents is already scoped to active statuses (LoadActiveAsync), so every entry here
            // is eligible: incidents that never reached MonitoringRecovery (RecoveryConfirmationCount == 1,
            // confirmed in a single pass) resolve directly, same as ones that went through it.
            foreach (var incident in incidents)
            {
                await ResolveAsync(incident, check, result, isMaintenance, now, cancellationToken);
            }
        }
    }

    private async Task<HashSet<Guid>> InterruptRecoveryAsync(
        LogicalCheck check,
        NormalizedCheckResult result,
        IEnumerable<Incident> incidents,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var observed = ObservedIssueKeys(result);
        var interruptedIncidentIds = new HashSet<Guid>();
        foreach (var incident in incidents.Where(candidate =>
                     candidate.Status == IncidentStatuses.MonitoringRecovery
                     && observed.Contains(candidate.IssueKey)))
        {
            interruptedIncidentIds.Add(incident.Id);
            var before = IncidentLifecycleService.Snapshot(incident);
            var decision = IncidentLifecycleEngine.Evaluate(new(
                incident.Status,
                IncidentLifecycleAction.InterruptRecovery,
                WasAcknowledged: incident.AcknowledgedAt is not null));
            var previousStatus = incident.Status;
            incident.Status = decision.NewStatus!;
            incident.RecoveryStartedAt = null;
            incident.RecoveryDurationMs = null;
            incident.Version++;
            AddStatusEvent(incident, previousStatus, incident.Status, now);
            AddEvidence(incident, check, result, IncidentEvidenceTypes.Failure, "RecoveryInterrupted", now);
            AddEvidenceEvent(incident, "Failure evidence interrupted recovery.", now);
            await WriteAuditAsync(
                IncidentAuditAction.RecoveryInterrupted,
                before,
                incident,
                now,
                cancellationToken);
        }

        return interruptedIncidentIds;
    }

    private async Task<Incident> OpenAsync(
        LogicalCheck check,
        NormalizedCheckResult result,
        string issueKey,
        string severity,
        bool isMaintenance,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previous = await FindPreviousAsync(check.EndpointMonitorId, issueKey, result.MeasuredAt, cancellationToken);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = check.EndpointMonitorId,
            OwnerSubjectId = check.EndpointMonitor.Endpoint.OwnerSubjectId
                ?? check.EndpointMonitor.Endpoint.Environment.Website.OwnerSubjectId,
            PreviousIncidentId = previous?.Id,
            IssueKey = issueKey,
            Severity = severity,
            Status = IncidentStatuses.Open,
            RecurrenceCount = previous is null ? 0 : previous.RecurrenceCount + 1,
            OpenedAt = result.MeasuredAt,
            Version = 1
        };
        dbContext.Incidents.Add(incident);
        var openedEvent = AddOpenedEvent(incident, now);
        AddEvidence(incident, check, result, IncidentEvidenceTypes.Opening, "ConfirmationThreshold", now);
        AddEvidenceEvent(incident, "Opening evidence recorded.", now);
        await auditTrail.RecordIncidentMutationAsync(
            SystemContext(now),
            IncidentAuditAction.Opened,
            null,
            IncidentLifecycleService.Snapshot(incident),
            cancellationToken);
        await notificationEventWriter.WriteAsync(
            incident,
            openedEvent.Id,
            NotificationSourceKinds.IncidentEvent,
            NotificationEventTypes.Opened,
            NotificationOccurrenceKeys.Opening(incident.Id),
            isMaintenance,
            now,
            cancellationToken);
        return incident;
    }

    private async Task BeginRecoveryAsync(
        Incident incident,
        LogicalCheck check,
        NormalizedCheckResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var decision = IncidentLifecycleEngine.Evaluate(new(
            incident.Status,
            IncidentLifecycleAction.BeginRecovery));
        if (!decision.Succeeded)
        {
            return;
        }

        var before = IncidentLifecycleService.Snapshot(incident);
        var previousStatus = incident.Status;
        incident.Status = decision.NewStatus!;
        incident.RecoveryStartedAt = result.MeasuredAt;
        incident.Version++;
        AddStatusEvent(incident, previousStatus, incident.Status, now);
        AddEvidence(incident, check, result, IncidentEvidenceTypes.Recovery, "RecoveryStarted", now);
        AddEvidenceEvent(incident, "First recovery pass recorded.", now);
        await WriteAuditAsync(
            IncidentAuditAction.RecoveryStarted,
            before,
            incident,
            now,
            cancellationToken);
    }

    private async Task ResolveAsync(
        Incident incident,
        LogicalCheck check,
        NormalizedCheckResult result,
        bool isMaintenance,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? resolutionCategory = null,
        string? resolutionNote = null)
    {
        var decision = IncidentLifecycleEngine.Evaluate(new(
            incident.Status,
            IncidentLifecycleAction.ConfirmRecovery));
        if (!decision.Succeeded)
        {
            return;
        }

        var before = IncidentLifecycleService.Snapshot(incident);
        var previousStatus = incident.Status;
        incident.Status = decision.NewStatus!;
        incident.ResolutionCategory = resolutionCategory ?? decision.ResolutionCategory;
        incident.ResolutionNote = resolutionNote ?? decision.ResolutionNote;
        incident.ResolvedAt = result.MeasuredAt;
        incident.RecoveryDurationMs = IncidentLifecycleEngine.DurationMilliseconds(
            incident.RecoveryStartedAt ?? result.MeasuredAt,
            result.MeasuredAt);
        incident.OutageDurationMs = IncidentLifecycleEngine.DurationMilliseconds(
            incident.OpenedAt,
            result.MeasuredAt);
        incident.Version++;
        var statusEvent = AddStatusEvent(incident, previousStatus, incident.Status, now);
        AddEvidence(incident, check, result, IncidentEvidenceTypes.Recovery, "RecoveryConfirmed", now);
        AddEvidence(incident, check, result, IncidentEvidenceTypes.Resolution, "AutomaticRecovery", now);
        AddEvidenceEvent(incident, "Recovery and resolution evidence recorded.", now);
        await WriteAuditAsync(
            IncidentAuditAction.Resolved,
            before,
            incident,
            now,
            cancellationToken);
        await notificationEventWriter.WriteAsync(
            incident,
            statusEvent.Id,
            NotificationSourceKinds.IncidentEvent,
            NotificationEventTypes.Recovered,
            NotificationOccurrenceKeys.Recovery(statusEvent.Id),
            isMaintenance,
            now,
            cancellationToken);
    }

    private async Task RecordEvidenceMutationAsync(
        Incident incident,
        LogicalCheck check,
        NormalizedCheckResult result,
        string evidenceType,
        string evidenceRole,
        IncidentAuditAction action,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? observedSeverity = null)
    {
        var before = IncidentLifecycleService.Snapshot(incident);
        incident.Version++;
        // A certificate keeps one incident as it crosses expiry bands (its fingerprint, and so
        // its issue key, does not change), so the incident escalates in place. It never
        // de-escalates: an incident that reached critical stays critical until it resolves.
        var escalated = observedSeverity is not null
            && FindingSeverities.Rank(observedSeverity) > FindingSeverities.Rank(incident.Severity);
        if (escalated)
        {
            AddEvent(
                incident,
                IncidentEventTypes.NoteAdded,
                now,
                note: $"Severity escalated from {incident.Severity} to {observedSeverity}.");
            incident.Severity = observedSeverity!;
        }

        AddEvidence(incident, check, result, evidenceType, evidenceRole, now);
        AddEvidenceEvent(incident, $"{evidenceType} evidence recorded.", now);
        await WriteAuditAsync(action, before, incident, now, cancellationToken);
    }

    private Task<List<Incident>> LoadActiveAsync(Guid monitorId, CancellationToken cancellationToken) =>
        dbContext.Incidents
            .FromSqlInterpolated($"""
                SELECT * FROM web_health.incident
                WHERE endpoint_monitor_id = {monitorId}
                  AND status = ANY({IncidentStatuses.Active.ToArray()})
                FOR UPDATE
                """)
            .Include(incident => incident.Events)
            .ToListAsync(cancellationToken);

    private Task<Incident?> FindPreviousAsync(
        Guid monitorId,
        string issueKey,
        DateTimeOffset openedAt,
        CancellationToken cancellationToken) =>
        dbContext.Incidents
            .Where(incident => incident.EndpointMonitorId == monitorId
                && incident.IssueKey == issueKey
                && incident.Status == IncidentStatuses.Closed
                && incident.ClosedAt != null
                && incident.ClosedAt <= openedAt
                && incident.ClosedAt >= openedAt - IncidentLifecycleEngine.RecurrenceWindow)
            .OrderByDescending(incident => incident.ClosedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private void AddEvidence(
        Incident incident,
        LogicalCheck check,
        NormalizedCheckResult result,
        string evidenceType,
        string evidenceRole,
        DateTimeOffset now) =>
        dbContext.IncidentEvidence.Add(new IncidentEvidence
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            EndpointMonitorId = incident.EndpointMonitorId,
            LogicalCheckId = check.Id,
            EvidenceType = evidenceType,
            EvidenceRole = evidenceRole,
            BoundedSnapshot = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                result.Outcome,
                result.FailureCategory,
                result.MeasuredAt
            }, SerializerOptions),
            CapturedAt = now
        });

    private IncidentEvent AddOpenedEvent(Incident incident, DateTimeOffset now) =>
        AddEvent(incident, IncidentEventTypes.Opened, now, toStatus: IncidentStatuses.Open);

    private IncidentEvent AddStatusEvent(
        Incident incident,
        string fromStatus,
        string toStatus,
        DateTimeOffset now) =>
        AddEvent(incident, IncidentEventTypes.StatusChanged, now, fromStatus, toStatus);

    private IncidentEvent AddEvidenceEvent(Incident incident, string note, DateTimeOffset now) =>
        AddEvent(incident, IncidentEventTypes.EvidenceRecorded, now, note: note);

    private IncidentEvent AddEvent(
        Incident incident,
        string eventType,
        DateTimeOffset now,
        string? fromStatus = null,
        string? toStatus = null,
        string? note = null)
    {
        var incidentEvent = new IncidentEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            SequenceNumber = NextSequence(incident.Id),
            EventType = eventType,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            BoundedNote = note,
            OccurredAt = now
        };
        dbContext.IncidentEvents.Add(incidentEvent);
        return incidentEvent;
    }

    private long NextSequence(Guid incidentId) =>
        dbContext.IncidentEvents.Local
            .Where(item => item.IncidentId == incidentId)
            .Select(item => item.SequenceNumber)
            .DefaultIfEmpty()
            .Max() + 1;

    private Task WriteAuditAsync(
        IncidentAuditAction action,
        IncidentAuditSnapshot before,
        Incident incident,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditTrail.RecordIncidentMutationAsync(
            SystemContext(now),
            action,
            before,
            IncidentLifecycleService.Snapshot(incident),
            cancellationToken);

    private static IncidentAuditWriteContext SystemContext(DateTimeOffset now) =>
        new(null, "system", now);

    private static HashSet<string> ObservedIssueKeys(NormalizedCheckResult result) =>
        CheckResultIssues.Observe(result, 1)
            .Select(issue => issue.IssueKey)
            .ToHashSet(StringComparer.Ordinal);
}
