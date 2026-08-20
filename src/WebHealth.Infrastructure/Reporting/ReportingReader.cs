using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Domain.Health;
using WebHealth.Domain.Incidents;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Reporting;

/// <summary>
/// The one authorized query layer behind every reporting surface (AC-11).
/// </summary>
/// <remarks>
/// <para>
/// The shape is deliberate. <see cref="SelectMonitors" /> is the only place a filter or a
/// visibility scope is applied, and every public method composes from it with the same
/// <see cref="ReportQuery" />. Nothing downstream filters again, so no two surfaces can select
/// different records: the screen, the export, the certificate card, the diagnostics card and the
/// incident list are one selection reported five ways.
/// </para>
/// <para>
/// Aggregation runs in PostgreSQL rather than in memory. The percentiles need
/// <c>percentile_cont</c>, and pulling every sample back to compute uptime client-side would
/// make a year-long window a multi-million-row transfer.
/// </para>
/// <para>
/// One instant is read per call and used for visibility scoping and for every "is this overdue"
/// comparison, so the freshness a screen displays is the instant its data was actually selected
/// at rather than an approximation of it.
/// </para>
/// </remarks>
internal sealed class ReportingReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    OwnerSubjectNames ownerNames,
    TimeProvider timeProvider) : IReportingReader
{
    /// <summary>How many expiring certificates the dashboard names rather than only counts.</summary>
    private const int AttentionListCount = 8;

    /// <summary>
    /// How far past its due slot a monitor may sit before it is reported as overdue. One
    /// dispatch sweep plus room for a slow one, so an ordinary sweep never looks like a fault.
    /// </summary>
    private static readonly TimeSpan OverdueGrace = TimeSpan.FromMinutes(10);

    public async Task<ReportDataset> QueryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var selection = SelectMonitors(query, access, now);
        var totalCount = await CountAsync(selection, cancellationToken);

        // The page the reader actually served is the page it reports back, so a request for
        // page 999,999 renders "page 2 of 2" rather than an unreachable page number with
        // pagination links that go nowhere.
        var effectiveQuery = query.WithPaging(EffectivePage(query, totalCount));
        var page = await PageAsync(selection, effectiveQuery, cancellationToken);
        var monitorIds = await MonitorIdsAsync(selection, cancellationToken);

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
        // ForExport re-slices the caller's filter rather than trusting it to have been sliced:
        // an export is the whole filtered set, never the page the screen happened to be on.
        var exportQuery = query.ForExport();
        var now = timeProvider.GetUtcNow();
        var selection = SelectMonitors(exportQuery, access, now);
        var totalCount = await CountAsync(selection, cancellationToken);
        var page = await PageAsync(selection, exportQuery, cancellationToken);
        return new(exportQuery, await BuildRowsAsync(exportQuery, page, cancellationToken), totalCount);
    }

    /// <summary>
    /// BR-C04 across the filtered set. The band is re-derived from the day count stored with
    /// the observation rather than from the current clock, so the dashboard, the endpoint page
    /// and the check that raised the finding all report the same severity for one observation.
    /// </summary>
    public async Task<ReportCertificateExpiry> QueryCertificateExpiryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var totalCount = await CountAsync(SelectMonitors(query, access, now), cancellationToken);

        // The certificate subset is selected by the same method the whole selection is, with the
        // monitor type as one more filter applied where every other filter is applied. Narrowing
        // the projected rows afterwards instead would put a predicate on a constructed record,
        // which the provider cannot translate once a visibility scope is in play.
        var certificateMonitors = await SelectMonitors(
                query, access, now, SslMonitorIdentity.MonitorType)
            .ToArrayAsync(cancellationToken);
        var notApplicable = totalCount - certificateMonitors.Length;
        if (certificateMonitors.Length == 0)
        {
            return ReportCertificateExpiry.Empty with { NotApplicableCount = notApplicable };
        }

        var monitorIds = certificateMonitors.Select(monitor => monitor.EndpointMonitorId).ToArray();
        var latest = await dbContext.CertificateObservations.AsNoTracking()
            .Where(observation => monitorIds.Contains(observation.EndpointMonitorId))
            .GroupBy(observation => observation.EndpointMonitorId)
            .Select(group => group
                .OrderByDescending(observation => observation.ObservedAt)
                .ThenByDescending(observation => observation.LogicalCheckId)
                .First())
            .ToArrayAsync(cancellationToken);
        var byMonitor = latest.ToDictionary(observation => observation.EndpointMonitorId);

        var items = certificateMonitors
            .Where(monitor => byMonitor.ContainsKey(monitor.EndpointMonitorId))
            .Select(monitor =>
            {
                var observation = byMonitor[monitor.EndpointMonitorId];
                var isValid = observation.ValidationCategory == nameof(TlsValidationCategory.Valid);
                return new CertificateExpiryItem(
                    monitor.EndpointId,
                    monitor.EndpointDisplayUrl,
                    monitor.ClientName,
                    monitor.EnvironmentName,
                    observation.NotAfter,
                    observation.DaysRemaining,
                    observation.ValidationCategory,
                    isValid,
                    SelectExpirySeverity(observation.ValidationCategory, observation.DaysRemaining),
                    observation.ObservedAt);
            })
            .ToArray();

        return new(
            notApplicable,
            certificateMonitors.Length - items.Length,
            // Only a certificate that is valid *and* outside every band is healthy. An invalid
            // one is counted as invalid whether or not it also has an expiry band to report.
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
                .ToArray());
    }

    /// <summary>
    /// BR-C04, re-derived from the stored day count.
    /// </summary>
    /// <remarks>
    /// An expired certificate is critical: it has a negative day count and lands in the critical
    /// band by the same comparison every other band uses. A certificate that is invalid for some
    /// other reason — not yet valid, hostname mismatch, untrusted — has no meaningful expiry
    /// band, so it reports <c>None</c> here and is counted as invalid instead. The two are
    /// different facts and are never merged into one number.
    /// </remarks>
    private static CertificateExpirySeverity SelectExpirySeverity(
        string validationCategory,
        int daysRemaining)
    {
        if (validationCategory == nameof(TlsValidationCategory.Expired))
        {
            return CertificateExpirySeverity.Critical;
        }

        return validationCategory == nameof(TlsValidationCategory.Valid)
            ? CertificateExpiry.SelectSeverity(daysRemaining, CertificateExpiryThresholds.Default)
            : CertificateExpirySeverity.None;
    }

    public async Task<ReportDiagnostics> QueryDiagnosticsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var selection = SelectMonitors(query, access, now);
        await GuardSelectionSizeAsync(selection, cancellationToken);
        var monitorIds = await MonitorIdsAsync(selection, cancellationToken);
        if (monitorIds.Count == 0)
        {
            return ReportDiagnostics.Empty;
        }

        var overdueBefore = now - OverdueGrace;
        var scheduling = await dbContext.EndpointMonitors.AsNoTracking()
            .Where(monitor => monitorIds.Contains(monitor.Id))
            .Select(monitor => new
            {
                LifecycleEnabled = monitor.Endpoint.IsEnabled
                    && monitor.Endpoint.DeletedAt == null
                    && monitor.Endpoint.Environment.IsActive
                    && monitor.Endpoint.Environment.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.IsEnabled
                    && monitor.Endpoint.Environment.Website.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.Client.IsActive
                    && monitor.Endpoint.Environment.Website.Client.DeletedAt == null,
                monitor.SchedulingEnabled,
                monitor.IsEnabled,
                // Run on demand only. IsEnabled is the pause switch for a scheduled monitor and
                // carries no meaning once scheduling is off, so it is not read here: a monitor
                // paused before scheduling was turned off would otherwise land in none of the
                // three states, and the total would silently fall short of what it claims.
                // A monitor is late only once its slot has been missed by more than the grace
                // period; the moment a slot arrives it is merely due.
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
            .OrderByDescending(result => result.MeasuredAt)
            .Select(result => (DateTimeOffset?)result.MeasuredAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new(
            scheduling.Count(monitor => monitor.LifecycleEnabled
                && monitor.SchedulingEnabled && monitor.IsEnabled),
            scheduling.Count(monitor => !monitor.LifecycleEnabled
                || monitor.SchedulingEnabled && !monitor.IsEnabled),
            scheduling.Count(monitor => monitor.LifecycleEnabled && !monitor.SchedulingEnabled),
            scheduling.Count(monitor => monitor.LifecycleEnabled
                && monitor.SchedulingEnabled && monitor.IsEnabled
                && monitor.NextDueAt < overdueBefore),
            inFlight,
            work.Where(item => item.State == DurableWorkStates.Failed).Sum(item => item.Count),
            lastCompleted);
    }

    public async Task<IReadOnlyList<ReportIncidentItem>> QueryActiveIncidentsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var selection = SelectMonitors(query, access, now);
        await GuardSelectionSizeAsync(selection, cancellationToken);
        var monitorIds = await MonitorIdsAsync(selection, cancellationToken);
        if (monitorIds.Count == 0)
        {
            return [];
        }

        // The same active statuses the summary counts, over the same monitors, so the list and
        // the count can never describe different sets.
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
            row.Severity,
            row.Status,
            row.OpenedAt,
            row.AcknowledgedAt,
            names.GetValueOrDefault(row.OwnerSubjectId, "Unknown owner"))).ToArray();
    }

    /// <summary>
    /// The filter and the visibility scope, composed exactly once, as an unexecuted query.
    /// </summary>
    /// <remarks>
    /// It stays an <see cref="IQueryable{T}" /> so counting, paging and identifier collection all
    /// run in PostgreSQL against the same composition. The ordering is a total order — the
    /// monitor identifier is the final tiebreaker — so paging is stable and page N of the screen
    /// and the corresponding slice of an export contain the same records in the same sequence.
    /// </remarks>
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
            // The effective owner, matching how ownership is resolved everywhere else: an
            // endpoint without its own owner is owned by its website.
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

        // The ordering is applied to the monitor's own columns before anything is projected.
        // Ordering the projection instead makes the sort keys expressions over a constructed
        // record, which the provider cannot translate once the visibility scope has contributed
        // its own subqueries - so the dashboard worked for global access and failed for every
        // other role.
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
                // BR-U06: the dashboard reads the latest confirmed state. Health is one row per
                // monitor, so it is read through the navigation as a join rather than as two
                // correlated subqueries per row. A monitor that has never confirmed anything is
                // Unknown, which is a state to show rather than a row to hide.
                //
                // A disabled monitor reports Disabled instead: its stored status is the state it
                // was in when checking stopped, and presenting that as current would say
                // "Healthy" about an endpoint nobody is checking. The stored value is carried in
                // the next column so the row can still say what it was.
                // Mirrors MonitorDisplayStatus, which the filter and the health totals use.
                !monitor.IsEnabled
                    || !monitor.Endpoint.IsEnabled
                    || monitor.Endpoint.DeletedAt != null
                    || !monitor.Endpoint.Environment.IsActive
                    || monitor.Endpoint.Environment.DeletedAt != null
                    || !monitor.Endpoint.Environment.Website.IsEnabled
                    || monitor.Endpoint.Environment.Website.DeletedAt != null
                    || !monitor.Endpoint.Environment.Website.Client.IsActive
                    || monitor.Endpoint.Environment.Website.Client.DeletedAt != null
                    ? EndpointHealthStatuses.Disabled
                    : monitor.EndpointHealth == null
                        ? EndpointHealthStatuses.Unknown
                        : monitor.EndpointHealth.ConfirmedStatus,
                (monitor.IsEnabled && monitor.Endpoint.IsEnabled
                    && monitor.Endpoint.DeletedAt == null
                    && monitor.Endpoint.Environment.IsActive
                    && monitor.Endpoint.Environment.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.IsEnabled
                    && monitor.Endpoint.Environment.Website.DeletedAt == null
                    && monitor.Endpoint.Environment.Website.Client.IsActive
                    && monitor.Endpoint.Environment.Website.Client.DeletedAt == null)
                    || monitor.EndpointHealth == null
                    ? null
                    : monitor.EndpointHealth.ConfirmedStatus,
                monitor.EndpointHealth == null
                    ? null
                    : (DateTimeOffset?)monitor.EndpointHealth.ConfirmedAt,
                dbContext.Incidents.Count(incident =>
                    incident.EndpointMonitorId == monitor.Id
                    && IncidentStatuses.Active.Contains(incident.Status))));

        return projected;
    }

    private static async Task<int> CountAsync(
        IQueryable<MonitorRow> selection,
        CancellationToken cancellationToken)
    {
        var count = await selection.CountAsync(cancellationToken);
        return count > ReportQueryNormalizer.MaximumMonitors
            ? throw new ReportTooLargeException(ReportQueryNormalizer.MaximumMonitors)
            : count;
    }

    private static Task GuardSelectionSizeAsync(
        IQueryable<MonitorRow> selection,
        CancellationToken cancellationToken) =>
        CountAsync(selection, cancellationToken);

    /// <summary>
    /// The requested page, clamped to what the selection actually has. A page beyond the end
    /// serves the last one rather than an empty table with no way back.
    /// </summary>
    private static int EffectivePage(ReportQuery query, int totalCount) =>
        Math.Clamp(query.Page, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)query.PageSize)));

    /// <summary>Paging runs in PostgreSQL, so a page costs one page rather than one selection.</summary>
    private static Task<MonitorRow[]> PageAsync(
        IQueryable<MonitorRow> selection,
        ReportQuery query,
        CancellationToken cancellationToken) =>
        selection
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

    /// <summary>
    /// The identifiers the aggregates run over: the whole selection, not one page, because the
    /// cards and the trend describe the filter rather than the page being viewed. The count
    /// guard above bounds this array.
    /// </summary>
    private static async Task<IReadOnlyList<Guid>> MonitorIdsAsync(
        IQueryable<MonitorRow> selection,
        CancellationToken cancellationToken) =>
        await selection
            .Select(row => row.EndpointMonitorId)
            .Take(ReportQueryNormalizer.MaximumMonitors)
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
            var sample = samples.GetValueOrDefault(monitor.EndpointMonitorId) ?? SampleAggregate.Empty;
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
                // Null rather than a guess when one monitor's window spans two sources; the
                // dataset-level comparability warning is what reports the mixture (BR-P05).
                sample.SingleMonitorSource);
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
            ? SampleAggregate.Empty
            : (await LoadSamplesAsync(query, monitorIds, groupByMonitor: false, cancellationToken))
                .Values.SingleOrDefault() ?? SampleAggregate.Empty;
        var health = monitorIds.Count == 0 ? [] : await LoadHealthCountsAsync(monitorIds, cancellationToken);

        return new ReportSummary(
            totalCount,
            monitorIds.Count == 0 ? 0 : await CountDistinctEndpointsAsync(monitorIds, cancellationToken),
            health.GetValueOrDefault(EndpointHealthStatuses.Healthy),
            health.GetValueOrDefault(EndpointHealthStatuses.Warning),
            health.GetValueOrDefault(EndpointHealthStatuses.Critical),
            health.GetValueOrDefault(EndpointHealthStatuses.Unknown),
            health.GetValueOrDefault(EndpointHealthStatuses.Disabled),
            monitorIds.Count == 0 ? 0 : await CountActiveIncidentsAsync(monitorIds, cancellationToken),
            totals.ToUptime(),
            totals.ToResponseTimes(),
            await AssessComparabilityAsync(query, monitorIds, cancellationToken));
    }

    /// <summary>
    /// The cards describe the whole filter, not the page in view, so the counts are aggregated
    /// in the database over every selected monitor rather than over the rows that fit on screen.
    /// </summary>
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

    /// <summary>
    /// BR-P05, over exactly the samples this report aggregated. Reusing the same assessment the
    /// check-history page uses means one definition of "comparable" across the application.
    /// </summary>
    private async Task<ComparabilityAssessment> AssessComparabilityAsync(
        ReportQuery query,
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        if (monitorIds.Count == 0)
        {
            return PerformanceComparability.Evaluate([], configurationChanged: false);
        }

        // "The configuration changed" is a question about one monitor over time, and it is
        // grouped by monitor for that reason. A fingerprint hashes the endpoint's normalized URL
        // among other things, so two different monitors always carry different fingerprints:
        // comparing them across a fleet answers "are these different monitors?", which is always
        // yes, and the warning was therefore on for every report covering more than one endpoint.
        // A warning that is always on carries no information.
        const string sql = """
            WITH per_monitor AS (
                SELECT
                    result.endpoint_monitor_id,
                    array_agg(DISTINCT result.monitor_source) AS sources,
                    min(snapshot.configuration_fingerprint)
                        <> max(snapshot.configuration_fingerprint) AS changed
                FROM web_health.check_result AS result
                JOIN web_health.check_configuration_snapshot AS snapshot
                  ON snapshot.logical_check_id = result.logical_check_id
                WHERE result.endpoint_monitor_id = ANY(@monitor_ids)
                  AND result.measured_at >= @window_start
                  AND result.measured_at < @window_end
                  AND result.counts_for_uptime
                GROUP BY result.endpoint_monitor_id
            )
            SELECT
                (SELECT array_agg(DISTINCT source)
                 FROM per_monitor, unnest(per_monitor.sources) AS source) AS monitor_sources,
                (SELECT coalesce(bool_or(changed), false) FROM per_monitor) AS configuration_changed;
            """;
        await using var scope = await CreateCommandAsync(sql, query, monitorIds, cancellationToken);
        await using var reader = await scope.Command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0))
        {
            return PerformanceComparability.Evaluate([], configurationChanged: false);
        }

        return PerformanceComparability.Evaluate(
            reader.GetFieldValue<string[]>(0),
            reader.GetBoolean(1));
    }

    /// <summary>
    /// The uptime and percentile aggregate, and the only place the sample rules live.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eligibility is <c>counts_for_uptime</c>, which the finalizer already set to exclude manual
    /// runs, cancelled runs, maintenance-suppressed runs and certificate checks (BR-U02, BR-U03).
    /// Reapplying those rules here would be a second definition of eligibility that could drift
    /// from the one the data was written under.
    /// </para>
    /// <para>
    /// Every category below is written as what it <em>is</em> — <c>= 'Healthy'</c>,
    /// <c>= 'Warning'</c>, <c>= 'Critical'</c> — never as "not failed". A predicate phrased
    /// negatively silently absorbs any outcome added later, which is exactly how a new
    /// quality rule could end up counted as uptime without anyone deciding that it should be.
    /// </para>
    /// <para>
    /// Uptime is healthy over eligible (BR-U01). Percentiles run over samples that
    /// <em>responded</em> — healthy or warning — because BR-U05 asks for successful samples and a
    /// warning sample is a completed exchange whose duration is a real measurement. A failed
    /// exchange's duration is its timeout budget, and admitting it would drag P95 toward the
    /// timeout setting instead of describing how the site performs.
    /// </para>
    /// <para>
    /// The aggregate reads <c>check_result</c> alone. The monitor identity is carried on the
    /// sample, so the monitor filter and the window filter are both predicates on one table and
    /// one composite index answers them together. Reaching the monitor through
    /// <c>logical_check</c> instead put the two halves of the predicate on different tables,
    /// which no index can serve: every aggregate became a full scan of both tables joined by
    /// hash, and the measured dashboard sat at roughly twice its budget.
    /// </para>
    /// <para>
    /// <c>percentile_cont</c>, not <c>percentile_disc</c>: response time is a continuous
    /// quantity, and interpolating between the two samples that straddle the rank gives a P95
    /// that moves smoothly as samples arrive. The cost is that a reported percentile may be a
    /// millisecond value no single check produced, which is the normal trade for a continuous
    /// statistic and the reason the sample count is reported beside it.
    /// </para>
    /// </remarks>
    private async Task<Dictionary<Guid, SampleAggregate>> LoadSamplesAsync(
        ReportQuery query,
        IReadOnlyList<Guid> monitorIds,
        bool groupByMonitor,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT
                {(groupByMonitor ? "result.endpoint_monitor_id" : "NULL::uuid")} AS monitor_id,
                count(*) FILTER (WHERE {EligibleSample}) AS eligible,
                count(*) FILTER (WHERE {HealthySample}) AS healthy_samples,
                count(*) FILTER (WHERE {WarningSample}) AS warning_samples,
                count(*) FILTER (WHERE {DownSample}) AS down_samples,
                count(*) FILTER (WHERE NOT result.counts_for_uptime) AS excluded_samples,
                count(*) FILTER (WHERE {RespondedSample}) AS responded_samples,
                percentile_cont(0.5) WITHIN GROUP (ORDER BY result.total_duration_ms)
                    FILTER (WHERE {RespondedSample}) AS p50_ms,
                percentile_cont(0.95) WITHIN GROUP (ORDER BY result.total_duration_ms)
                    FILTER (WHERE {RespondedSample}) AS p95_ms,
                max(result.measured_at) AS last_measured_at,
                min(result.monitor_source) AS lowest_source,
                max(result.monitor_source) AS highest_source
            FROM web_health.check_result AS result
            WHERE result.endpoint_monitor_id = ANY(@monitor_ids)
              AND result.measured_at >= @window_start
              AND result.measured_at < @window_end
            {(groupByMonitor ? "GROUP BY result.endpoint_monitor_id" : string.Empty)};
            """;
        await using var scope = await CreateCommandAsync(sql, query, monitorIds, cancellationToken);
        await using var reader = await scope.Command.ExecuteReaderAsync(cancellationToken);
        var aggregates = new Dictionary<Guid, SampleAggregate>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var lowestSource = reader.IsDBNull(10) ? null : reader.GetString(10);
            var highestSource = reader.IsDBNull(11) ? null : reader.GetString(11);
            aggregates[reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0)] = new SampleAggregate(
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                string.Equals(lowestSource, highestSource, StringComparison.Ordinal) ? lowestSource : null);
        }

        return aggregates;
    }

    private const string EligibleSample = "result.counts_for_uptime";
    private const string HealthySample = "result.counts_for_uptime AND result.outcome = 'Healthy'";
    private const string WarningSample = "result.counts_for_uptime AND result.outcome = 'Warning'";
    private const string DownSample = "result.counts_for_uptime AND result.outcome = 'Critical'";

    private const string RespondedSample =
        "result.counts_for_uptime AND result.outcome IN ('Healthy', 'Warning')";

    /// <summary>
    /// The daily series behind the trend chart, over the same sample categories the summary
    /// uses, so a chart and the card above it can never disagree about the same window. Days are
    /// bucketed in UTC, matching the window's own boundaries (BR-U04).
    /// </summary>
    private async Task<IReadOnlyList<ReportTrendPoint>> BuildTrendAsync(
        ReportQuery query,
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        if (monitorIds.Count == 0)
        {
            return [];
        }

        var sql = $"""
            SELECT
                (result.measured_at AT TIME ZONE 'UTC')::date AS day,
                count(*) FILTER (WHERE {EligibleSample}) AS eligible,
                count(*) FILTER (WHERE {HealthySample}) AS healthy_samples,
                percentile_cont(0.5) WITHIN GROUP (ORDER BY result.total_duration_ms)
                    FILTER (WHERE {RespondedSample}) AS p50_ms,
                percentile_cont(0.95) WITHIN GROUP (ORDER BY result.total_duration_ms)
                    FILTER (WHERE {RespondedSample}) AS p95_ms
            FROM web_health.check_result AS result
            WHERE result.endpoint_monitor_id = ANY(@monitor_ids)
              AND result.measured_at >= @window_start
              AND result.measured_at < @window_end
              AND result.counts_for_uptime
            GROUP BY 1
            ORDER BY 1;
            """;
        await using var scope = await CreateCommandAsync(sql, query, monitorIds, cancellationToken);
        await using var reader = await scope.Command.ExecuteReaderAsync(cancellationToken);
        var points = new List<ReportTrendPoint>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var eligible = reader.GetInt64(1);
            var healthy = reader.GetInt64(2);
            points.Add(new(
                DateOnly.FromDateTime(reader.GetDateTime(0)),
                eligible,
                healthy,
                eligible == 0 ? null : Math.Round(healthy * 100d / eligible, 4, MidpointRounding.AwayFromZero),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4)));
        }

        return points;
    }

    /// <summary>
    /// Opens the context's own connection so these raw aggregates run on the same connection and
    /// ambient transaction as everything else the request does, then hands back a disposable that
    /// closes it again only if this call was the one that opened it.
    /// </summary>
    private async Task<NpgsqlCommandScope> CreateCommandAsync(
        string sql,
        ReportQuery query,
        IReadOnlyList<Guid> monitorIds,
        CancellationToken cancellationToken)
    {
        var wasClosed = dbContext.Database.GetDbConnection().State != ConnectionState.Open;
        if (wasClosed)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        var command = new NpgsqlCommand(
            sql,
            (NpgsqlConnection)dbContext.Database.GetDbConnection(),
            dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
        command.Parameters.AddWithValue(
            "monitor_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, monitorIds.ToArray());
        command.Parameters.AddWithValue("window_start", NpgsqlDbType.TimestampTz, query.WindowStart);
        command.Parameters.AddWithValue("window_end", NpgsqlDbType.TimestampTz, query.WindowEnd);
        return new(command, wasClosed ? dbContext : null);
    }

    private sealed record NpgsqlCommandScope(NpgsqlCommand Command, ApplicationDbContext? ContextToClose)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Command.DisposeAsync();
            if (ContextToClose is not null)
            {
                await ContextToClose.Database.CloseConnectionAsync();
            }
        }
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
        int ActiveIncidentCount);

    private sealed record SampleAggregate(
        long EligibleSamples,
        long HealthySamples,
        long WarningSamples,
        long DownSamples,
        long ExcludedSamples,
        long RespondedSamples,
        double? P50Ms,
        double? P95Ms,
        DateTimeOffset? LastMeasuredAt,
        string? SingleMonitorSource)
    {
        public static SampleAggregate Empty { get; } = new(0, 0, 0, 0, 0, 0, null, null, null, null);

        public ReportUptime ToUptime() =>
            new(EligibleSamples, HealthySamples, WarningSamples, DownSamples, ExcludedSamples);

        public ReportResponseTimes ToResponseTimes() => new(P50Ms, P95Ms, RespondedSamples);
    }
}
