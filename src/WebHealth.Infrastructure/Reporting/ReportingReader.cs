using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Application.Reporting;
using WebHealth.Domain.Health;
using WebHealth.Domain.Incidents;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Reporting;

internal sealed class ReportingReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    OwnerSubjectNames ownerNames,
    TimeProvider timeProvider,
    MonitoringSchedulingOptions schedulingOptions,
    IMonitoringWorkerReader workerReader) : IReportingReader
{
    private const int AttentionListCount = 8;

    private readonly Dictionary<SelectionKey, Task<Selection>> selections = [];
    private readonly RetainedReportSamples retainedSamples = new(dbContext);

    public async Task<ReportDataset> QueryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var selection = await ResolveSelectionAsync(query, access, cancellationToken);
        var totalCount = selection.TotalCount;

        var effectiveQuery = query.WithPaging(EffectivePage(query, totalCount));
        var page = await PageAsync(selection.Monitors, effectiveQuery, cancellationToken);
        var monitorIds = selection.MonitorIds;

        return new(
            effectiveQuery,
            await BuildSummaryAsync(effectiveQuery, monitorIds, page, totalCount, cancellationToken),
            await BuildRowsAsync(effectiveQuery, page, cancellationToken),
            await BuildTrendAsync(effectiveQuery, monitorIds, cancellationToken),
            totalCount);
    }

    public async Task<ReportExport> ExportAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var exportQuery = query.ForExport();
        var selection = await ResolveSelectionAsync(exportQuery, access, cancellationToken);
        var page = await PageAsync(selection.Monitors, exportQuery, cancellationToken);
        return new(
            exportQuery,
            await BuildRowsAsync(exportQuery, page, cancellationToken),
            selection.TotalCount);
    }

    public async Task<ReportCertificateExpiry> QueryCertificateExpiryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var selection = await ResolveSelectionAsync(query, access, cancellationToken);
        var totalCount = selection.TotalCount;

        var certificateMonitors = await SelectMonitors(
                query, access, selection.Now, SslMonitorIdentity.MonitorType)
            .ToArrayAsync(cancellationToken);
        var notApplicable = totalCount - certificateMonitors.Length;
        if (certificateMonitors.Length == 0)
        {
            return ReportCertificateExpiry.Empty with { NotApplicableCount = notApplicable };
        }

        var monitorIds = certificateMonitors.Select(monitor => monitor.EndpointMonitorId).ToArray();
        var latest = await LoadLatestCertificateObservationsAsync(monitorIds, cancellationToken);
        var byMonitor = latest.ToDictionary(item => item.EndpointMonitorId);

        var items = certificateMonitors
            .Where(monitor => byMonitor.ContainsKey(monitor.EndpointMonitorId))
            .Select(monitor =>
            {
                var recorded = byMonitor[monitor.EndpointMonitorId];
                var isValid = recorded.ValidationCategory == nameof(TlsValidationCategory.Valid);
                return new CertificateExpiryItem(
                    monitor.EndpointId,
                    monitor.EndpointDisplayUrl,
                    monitor.ClientName,
                    monitor.EnvironmentName,
                    recorded.NotAfter,
                    recorded.DaysRemaining,
                    recorded.ValidationCategory,
                    isValid,
                    CertificateExpiry.SelectSeverity(recorded.DaysRemaining,
                        new(recorded.WarningDays ?? 30, recorded.HighDays ?? 15, recorded.CriticalDays ?? 7)),
                    recorded.ObservedAt);
            })
            .ToArray();

        return new(
            notApplicable,
            certificateMonitors.Length - items.Length,
            items.Count(item => item.IsValid && item.Severity == CertificateExpirySeverity.None),
            items.Count(item => !item.IsValid),
            items.Count(item => item.Severity == CertificateExpirySeverity.Warning),
            items.Count(item => item.Severity == CertificateExpirySeverity.High),
            items.Count(item => item.Severity == CertificateExpirySeverity.Critical),
            items
                .Where(item => !item.IsValid || item.Severity != CertificateExpirySeverity.None)
                .OrderByDescending(item => !item.IsValid)
                .ThenBy(item => item.DaysRemaining)
                .ThenBy(item => item.EndpointDisplayUrl, StringComparer.Ordinal)
                .Take(AttentionListCount)
                .ToArray(),
            items.Count(item => !item.IsValid && item.Severity != CertificateExpirySeverity.None));
    }

    private async Task<IReadOnlyList<LatestCertificateObservation>> LoadLatestCertificateObservationsAsync(
        Guid[] monitorIds, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != System.Data.ConnectionState.Open;
        if (closeConnection) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT latest.endpoint_monitor_id, latest.not_after, latest.days_remaining,
                    latest.validation_category, latest.observed_at, latest.ssl_warning_expiry_days,
                    latest.ssl_high_expiry_days, latest.ssl_critical_expiry_days
                FROM unnest(@monitor_ids) AS monitor(id)
                CROSS JOIN LATERAL (
                    SELECT observation.endpoint_monitor_id, observation.not_after, observation.days_remaining,
                        observation.validation_category, observation.observed_at, snapshot.ssl_warning_expiry_days,
                        snapshot.ssl_high_expiry_days, snapshot.ssl_critical_expiry_days
                    FROM web_health.certificate_observation observation
                    JOIN web_health.check_result result ON result.logical_check_id = observation.logical_check_id
                        AND result.endpoint_monitor_id = observation.endpoint_monitor_id
                    JOIN web_health.check_configuration_snapshot snapshot
                        ON snapshot.logical_check_id = observation.logical_check_id
                    WHERE observation.endpoint_monitor_id = monitor.id
                        AND result.current_state_disposition = 'Current'
                    ORDER BY observation.observed_at DESC, observation.logical_check_id DESC
                    LIMIT 1
                ) latest
                """, connection, dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
            command.Parameters.AddWithValue("monitor_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, monitorIds);
            var observations = new List<LatestCertificateObservation>(monitorIds.Length);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                observations.Add(new(reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetInt32(2),
                    reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetInt32(7)));
            return observations;
        }
        finally
        {
            if (closeConnection) await connection.CloseAsync();
        }
    }

    private sealed record LatestCertificateObservation(Guid EndpointMonitorId, DateTimeOffset NotAfter,
        int DaysRemaining, string ValidationCategory, DateTimeOffset ObservedAt, int? WarningDays,
        int? HighDays, int? CriticalDays);

    public async Task<ReportDiagnostics> QueryDiagnosticsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var selection = await ResolveSelectionAsync(query, access, cancellationToken);
        var monitorIds = selection.MonitorIds;
        if (monitorIds.Count == 0)
        {
            return await AddEngineHealthAsync(ReportDiagnostics.Empty, access, selection.Now, cancellationToken);
        }

        var overdueBefore = selection.Now - schedulingOptions.DispatchDelayGrace;
        var scheduling = await dbContext.EndpointMonitors.AsNoTracking()
            .Where(monitor => monitorIds.Contains(monitor.Id))
            .Select(monitor => new
            {
                LifecycleEnabled = monitor.Endpoint.IsEnabled
                    && monitor.Endpoint.DeletedAt == null
                    && monitor.Endpoint.Environment.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.IsEnabled
                    && monitor.Endpoint.Environment.Website.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.Client.IsActive
                    && monitor.Endpoint.Environment.Website.Client.DeletedAt == null,
                monitor.SchedulingEnabled,
                monitor.IsEnabled,
                monitor.NextDueAt
            })
            .ToArrayAsync(cancellationToken);

        var work = await dbContext.DurableWork.AsNoTracking()
            .Where(item => monitorIds.Contains(item.LogicalCheck.EndpointMonitorId))
            .GroupBy(item => item.State)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        var inFlight = work
            .Where(item => item.State == DurableWorkStates.Pending
                || item.State == DurableWorkStates.Dispatching
                || item.State == DurableWorkStates.Enqueued
                || item.State == DurableWorkStates.Processing)
            .Sum(item => item.Count);

        var lastCompleted = await dbContext.CheckResults.AsNoTracking()
            .Where(result => monitorIds.Contains(result.EndpointMonitorId))
            .MaxAsync(result => (DateTimeOffset?)result.MeasuredAt, cancellationToken);
        var completionRows = await selection.Monitors.ToArrayAsync(cancellationToken);
        var lastScheduled = completionRows.Max(monitor => monitor.LastScheduledCompletionAt);
        var oldestQueued = await dbContext.DurableWork.AsNoTracking()
            .Where(item => monitorIds.Contains(item.LogicalCheck.EndpointMonitorId)
                && item.AvailableAt <= selection.Now
                && (item.State == DurableWorkStates.Pending || item.State == DurableWorkStates.Dispatching
                    || item.State == DurableWorkStates.Enqueued))
            .MinAsync(item => (DateTimeOffset?)item.AvailableAt, cancellationToken);
        var oldestDue = scheduling.Where(monitor => monitor.LifecycleEnabled && monitor.SchedulingEnabled
                && monitor.IsEnabled && monitor.NextDueAt < selection.Now)
            .Select(monitor => (DateTimeOffset?)monitor.NextDueAt).Min();
        var diagnostics = new ReportDiagnostics(
            scheduling.Count(monitor => monitor.LifecycleEnabled
                && monitor.SchedulingEnabled && monitor.IsEnabled),
            scheduling.Count(monitor => monitor.LifecycleEnabled && monitor.SchedulingEnabled && !monitor.IsEnabled),
            scheduling.Count(monitor => monitor.LifecycleEnabled && !monitor.SchedulingEnabled),
            scheduling.Count(monitor => monitor.LifecycleEnabled
                && monitor.SchedulingEnabled && monitor.IsEnabled
                && monitor.NextDueAt < overdueBefore),
            inFlight,
            work.Where(item => item.State == DurableWorkStates.Failed).Sum(item => item.Count),
            lastCompleted, oldestDue, oldestQueued, lastScheduled);
        return await AddEngineHealthAsync(diagnostics, access, selection.Now, cancellationToken);
    }

    private async Task<ReportDiagnostics> AddEngineHealthAsync(ReportDiagnostics diagnostics,
        RegistryAccessContext access, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var operations = await dbContext.MonitoringRuntimeStates.AsNoTracking().OrderBy(state => state.Operation)
                .Select(state => new MonitoringOperationStatus(state.Operation, state.LastStartedAt,
                    state.LastSucceededAt, state.LastFailedAt, state.LastDurationMs, state.FailureCategory,
                    state.ConsecutiveFailures)).ToArrayAsync(cancellationToken);
        var workers = workerReader.Read();
        var health = MonitoringEngineHealth.Evaluate(schedulingOptions.Enabled, operations, workers,
            diagnostics.OldestQueuedAt, diagnostics.OldestOverdueAt, schedulingOptions.DispatchDelayGrace, now);
        var detailed = access.Roles.Contains(ApplicationRoles.Administrator) || access.Roles.Contains(ApplicationRoles.Operations);
        return diagnostics with
        {
            EngineHealth = health,
            Runtime = detailed ? new(schedulingOptions.Enabled, workers, operations) : null
        };
    }

    public async Task<IReadOnlyList<ReportIncidentItem>> QueryActiveIncidentsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var selection = await ResolveSelectionAsync(query, access, cancellationToken);
        var monitorIds = selection.MonitorIds;
        if (monitorIds.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.Incidents.AsNoTracking()
            .Where(incident => monitorIds.Contains(incident.EndpointMonitorId)
                && IncidentStatuses.Active.Contains(incident.Status))
            .OrderByDescending(incident => incident.OpenedAt)
            .ThenBy(incident => incident.Id)
            .Take(Math.Max(limit, 1))
            .Select(incident => new
            {
                incident.Id,
                incident.EndpointMonitor.EndpointId,
                incident.EndpointMonitor.Endpoint.DisplayUrl,
                ClientName = incident.EndpointMonitor.Endpoint.Environment.Website.Client.Name,
                EnvironmentName = incident.EndpointMonitor.Endpoint.Environment.Name,
                incident.IssueKey,
                incident.EndpointMonitor.MonitorType,
                incident.Severity,
                incident.Status,
                incident.OpenedAt,
                incident.AcknowledgedAt,
                incident.OwnerSubjectId
            })
            .ToArrayAsync(cancellationToken);

        var names = await ownerNames.LoadAsync(rows.Select(row => row.OwnerSubjectId), cancellationToken);
        return rows.Select(row => new ReportIncidentItem(
            row.Id,
            row.EndpointId,
            row.DisplayUrl,
            row.ClientName,
            row.EnvironmentName,
            row.IssueKey,
            row.MonitorType,
            row.Severity,
            row.Status,
            row.OpenedAt,
            row.AcknowledgedAt,
            names.GetValueOrDefault(row.OwnerSubjectId, "Unknown owner"))).ToArray();
    }

    private IQueryable<MonitorRow> SelectMonitors(
        ReportQuery query,
        RegistryAccessContext access,
        DateTimeOffset now,
        string? restrictToMonitorType = null)
    {
        var endpoints = visibility
            .ApplyEndpointScope(dbContext.Endpoints.AsNoTracking(), access, now)
            .Where(endpoint => endpoint.DeletedAt == null);

        if (query.ClientId is { } clientId)
        {
            endpoints = endpoints.Where(endpoint => endpoint.Environment.Website.ClientId == clientId);
        }

        if (query.WebsiteId is { } websiteId)
        {
            endpoints = endpoints.Where(endpoint => endpoint.Environment.WebsiteId == websiteId);
        }

        if (query.EnvironmentId is { } environmentId)
        {
            endpoints = endpoints.Where(endpoint => endpoint.EnvironmentId == environmentId);
        }

        if (query.OwnerSubjectId is { } ownerSubjectId)
        {
            endpoints = endpoints.Where(endpoint =>
                (endpoint.OwnerSubjectId ?? endpoint.Environment.Website.OwnerSubjectId) == ownerSubjectId);
        }

        var monitors = endpoints
            .SelectMany(endpoint => endpoint.Monitors.Where(monitor => monitor.DeletedAt == null));

        if (query.MonitorType is { } monitorType)
        {
            monitors = monitors.Where(monitor => monitor.MonitorType == monitorType);
        }

        if (restrictToMonitorType is { } restriction)
        {
            monitors = monitors.Where(monitor => monitor.MonitorType == restriction);
        }

        if (query.HealthStatus is { } healthStatus)
        {
            monitors = monitors.Where(MonitorDisplayStatus.Matches(healthStatus));
        }

        var projected = monitors
            .OrderBy(monitor => monitor.Endpoint.Environment.Website.Client.Name)
            .ThenBy(monitor => monitor.Endpoint.Environment.Website.Name)
            .ThenBy(monitor => monitor.Endpoint.Environment.Name)
            .ThenBy(monitor => monitor.Endpoint.DisplayUrl)
            .ThenBy(monitor => monitor.MonitorType)
            .ThenBy(monitor => monitor.Id)
            .Select(monitor => new MonitorRow(
                monitor.Id,
                monitor.EndpointId,
                monitor.Endpoint.Environment.Website.Client.Name,
                monitor.Endpoint.Environment.Website.Name,
                monitor.Endpoint.Environment.Name,
                monitor.Endpoint.Environment.IsProduction,
                monitor.Endpoint.DisplayUrl,
                monitor.MonitorType,
                monitor.Endpoint.OwnerSubjectId
                    ?? monitor.Endpoint.Environment.Website.OwnerSubjectId,
                monitor.EndpointHealth == null || monitor.EndpointHealth.ConfirmedStatus == EndpointHealthStatuses.Disabled
                    ? EndpointHealthStatuses.Unknown : monitor.EndpointHealth.ConfirmedStatus,
                null,
                monitor.EndpointHealth == null
                    ? null
                    : (DateTimeOffset?)monitor.EndpointHealth.ConfirmedAt,
                dbContext.Incidents.Count(incident =>
                    incident.EndpointMonitorId == monitor.Id
                    && IncidentStatuses.Active.Contains(incident.Status)),
                monitor.Endpoint.IsEnabled && monitor.Endpoint.DeletedAt == null
                    && monitor.Endpoint.Environment.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.IsEnabled && monitor.Endpoint.Environment.Website.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.Client.IsActive && monitor.Endpoint.Environment.Website.Client.DeletedAt == null,
                monitor.SchedulingEnabled,
                monitor.IsEnabled,
                monitor.IntervalSeconds,
                monitor.NextDueAt,
                monitor.LogicalChecks.Where(check => check.Source == LogicalCheckSources.Scheduled
                    && check.State == LogicalCheckStates.Completed && check.Result != null
                    && check.Result.CurrentStateDisposition == "Current" && check.CompletedAt != null)
                    .OrderByDescending(check => check.CompletedAt).ThenByDescending(check => check.Id)
                    .Select(check => check.CompletedAt).FirstOrDefault()));

        return projected;
    }

    private Task<Selection> ResolveSelectionAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        var key = SelectionKey.For(query, access);
        if (selections.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var resolving = ResolveAsync();
        selections[key] = resolving;
        return resolving;

        async Task<Selection> ResolveAsync()
        {
            var now = timeProvider.GetUtcNow();
            var monitors = SelectMonitors(query, access, now);
            var totalCount = await monitors.CountAsync(cancellationToken);
            if (totalCount > ReportQueryNormalizer.MaximumMonitors)
            {
                throw new ReportTooLargeException(ReportQueryNormalizer.MaximumMonitors);
            }

            var monitorIds = await monitors
                .Select(row => row.EndpointMonitorId)
                .Take(ReportQueryNormalizer.MaximumMonitors)
                .ToArrayAsync(cancellationToken);

            return new Selection(now, monitors, totalCount, monitorIds);
        }
    }

    private sealed record SelectionKey(ReportQuery Query, Guid UserId, string Roles)
    {
        public static SelectionKey For(ReportQuery query, RegistryAccessContext access) => new(
            query,
            access.UserId,
            string.Join("|", access.Roles.OrderBy(role => role, StringComparer.Ordinal)));
    }

    private sealed record Selection(
        DateTimeOffset Now,
        IQueryable<MonitorRow> Monitors,
        int TotalCount,
        IReadOnlyList<Guid> MonitorIds);

    private static int EffectivePage(ReportQuery query, int totalCount) =>
        Math.Clamp(query.Page, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)query.PageSize)));

    private static Task<MonitorRow[]> PageAsync(
        IQueryable<MonitorRow> selection,
        ReportQuery query,
        CancellationToken cancellationToken) =>
        selection
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyList<ReportRow>> BuildRowsAsync(
        ReportQuery query,
        IReadOnlyList<MonitorRow> monitors,
        CancellationToken cancellationToken)
    {
        if (monitors.Count == 0)
        {
            return [];
        }

        var monitorIds = monitors.Select(monitor => monitor.EndpointMonitorId).ToArray();
        var samples = await LoadSamplesAsync(query, monitorIds, groupByMonitor: true, cancellationToken);
        var names = await ownerNames.LoadAsync(
            monitors.Select(monitor => monitor.OwnerSubjectId), cancellationToken);

        return monitors.Select(monitor =>
        {
            var sample = samples.GetValueOrDefault(monitor.EndpointMonitorId) ?? ReportSampleAggregate.Empty;
            return new ReportRow(
                monitor.EndpointMonitorId,
                monitor.EndpointId,
                monitor.ClientName,
                monitor.WebsiteName,
                monitor.EnvironmentName,
                monitor.IsProduction,
                monitor.EndpointDisplayUrl,
                monitor.MonitorType,
                names.GetValueOrDefault(monitor.OwnerSubjectId, "Unknown owner"),
                monitor.ConfirmedStatus,
                monitor.StatusBeforeDisabled,
                monitor.ConfirmedAt,
                sample.ToUptime(),
                sample.ToResponseTimes(),
                sample.LastMeasuredAt,
                monitor.ActiveIncidentCount,
                sample.SingleMonitorSource,
                MonitorOperationalState.Evaluate(monitor.ConfirmedStatus, monitor.LifecycleEligible,
                    monitor.SchedulingEnabled, monitor.IsEnabled, monitor.IntervalSeconds, monitor.NextDueAt,
                    monitor.LastScheduledCompletionAt, timeProvider.GetUtcNow(), schedulingOptions.DispatchDelayGrace),
                sample.ToHistory());
        }).ToArray();
    }

    private async Task<ReportSummary> BuildSummaryAsync(
        ReportQuery query,
        IReadOnlyList<Guid> monitorIds,
        IReadOnlyList<MonitorRow> page,
        int totalCount,
        CancellationToken cancellationToken)
    {
        var totals = monitorIds.Count == 0
            ? ReportSampleAggregate.Empty
            : (await LoadSamplesAsync(query, monitorIds, groupByMonitor: false, cancellationToken))
                .Values.SingleOrDefault() ?? ReportSampleAggregate.Empty;
        var health = monitorIds.Count == 0 ? [] : await LoadHealthCountsAsync(monitorIds, cancellationToken);

        return new ReportSummary(
            totalCount,
            monitorIds.Count == 0 ? 0 : await CountDistinctEndpointsAsync(monitorIds, cancellationToken),
            health.GetValueOrDefault(EndpointHealthStatuses.Healthy),
            health.GetValueOrDefault(EndpointHealthStatuses.Warning),
            health.GetValueOrDefault(EndpointHealthStatuses.Critical),
            health.GetValueOrDefault(EndpointHealthStatuses.Unknown),
            await dbContext.EndpointMonitors.AsNoTracking().CountAsync(monitor => monitorIds.Contains(monitor.Id)
                && (!monitor.Endpoint.IsEnabled || monitor.Endpoint.DeletedAt != null
                    || monitor.Endpoint.Environment.DeletedAt != null
                    || !monitor.Endpoint.Environment.Website.IsEnabled || monitor.Endpoint.Environment.Website.DeletedAt != null
                    || !monitor.Endpoint.Environment.Website.Client.IsActive || monitor.Endpoint.Environment.Website.Client.DeletedAt != null),
                cancellationToken),
            monitorIds.Count == 0 ? 0 : await CountActiveIncidentsAsync(monitorIds, cancellationToken),
            totals.ToUptime(),
            totals.ToResponseTimes(),
            await AssessComparabilityAsync(query, monitorIds, cancellationToken),
            totals.ToHistory());
    }

    private async Task<Dictionary<string, int>> LoadHealthCountsAsync(
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.EndpointMonitors.AsNoTracking()
            .Where(monitor => monitorIds.Contains(monitor.Id))
            .GroupBy(MonitorDisplayStatus.Projection)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        return counts.ToDictionary(item => item.Status, item => item.Count, StringComparer.Ordinal);
    }

    private Task<int> CountDistinctEndpointsAsync(
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken) =>
        dbContext.EndpointMonitors.AsNoTracking()
            .Where(monitor => monitorIds.Contains(monitor.Id))
            .Select(monitor => monitor.EndpointId)
            .Distinct()
            .CountAsync(cancellationToken);

    private Task<int> CountActiveIncidentsAsync(
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken) =>
        dbContext.Incidents.AsNoTracking()
            .CountAsync(
                incident => monitorIds.Contains(incident.EndpointMonitorId)
                    && IncidentStatuses.Active.Contains(incident.Status),
                cancellationToken);

    private Task<ComparabilityAssessment> AssessComparabilityAsync(
        ReportQuery query, IReadOnlyList<Guid> monitorIds, CancellationToken cancellationToken) =>
        retainedSamples.AssessComparabilityAsync(query, monitorIds, cancellationToken);
    private async Task<Dictionary<Guid, ReportSampleAggregate>> LoadSamplesAsync(
        ReportQuery query, IReadOnlyList<Guid> monitorIds, bool groupByMonitor, CancellationToken cancellationToken)
    {
        var samples = await retainedSamples.LoadAsync(query, monitorIds,
            groupByMonitor ? ReportSampleGrouping.Monitor : ReportSampleGrouping.Summary, cancellationToken);
        return samples.ToDictionary(item => item.Key.Length == 0 ? Guid.Empty : Guid.Parse(item.Key), item => item.Value);
    }

    private async Task<IReadOnlyList<ReportTrendPoint>> BuildTrendAsync(
        ReportQuery query, IReadOnlyList<Guid> monitorIds, CancellationToken cancellationToken)
    {
        var samples = await retainedSamples.LoadAsync(query, monitorIds, ReportSampleGrouping.Day, cancellationToken);
        return samples.Where(item => item.Value.EligibleSamples > 0).OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
        {
            var uptime = item.Value.ToUptime();
            var response = item.Value.ToResponseTimes();
            return new ReportTrendPoint(DateOnly.ParseExact(item.Key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                uptime.EligibleSamples, uptime.HealthySamples + uptime.WarningSamples, uptime.Percentage,
                response.P50Ms, response.P95Ms, response.IsApproximate);
        }).ToArray();
    }
    private sealed record MonitorRow(
        Guid EndpointMonitorId,
        Guid EndpointId,
        string ClientName,
        string WebsiteName,
        string EnvironmentName,
        bool IsProduction,
        string EndpointDisplayUrl,
        string MonitorType,
        Guid OwnerSubjectId,
        string ConfirmedStatus,
        string? StatusBeforeDisabled,
        DateTimeOffset? ConfirmedAt,
        int ActiveIncidentCount,
        bool LifecycleEligible,
        bool SchedulingEnabled,
        bool IsEnabled,
        int IntervalSeconds,
        DateTimeOffset NextDueAt,
        DateTimeOffset? LastScheduledCompletionAt);

}
