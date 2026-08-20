using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Reporting;

/// <summary>
/// The single authorized query layer behind every reporting surface. Every method composes the
/// same filter and the same visibility scope over the same selected monitors; they differ only
/// in what they report about them. Nothing on a reporting screen may be read any other way,
/// which is what stops one card answering a different question from the card beside it (AC-11).
/// </summary>
public interface IReportingReader
{
    /// <summary>Cards, one page of rows, and the trend series for the chart endpoints.</summary>
    Task<ReportDataset> QueryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The whole filtered set, for the export. Paging is still bounded — the export's page size
    /// is the same limit the selection refuses to exceed — so this is never a truncated file and
    /// never an unbounded scan.
    /// </summary>
    Task<ReportExport> ExportAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Certificate expiry across the same filtered, visible monitors (BR-C04). Kept apart from
    /// <see cref="QueryAsync" /> because expiry is a property of the certificate presented now,
    /// not of the samples inside the reporting window: narrowing the window must not change how
    /// soon a certificate expires.
    /// </summary>
    Task<ReportCertificateExpiry> QueryCertificateExpiryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pipeline signals for the same filtered, visible monitors: whether checks are actually
    /// running, not whether the sites are healthy. A dashboard that shows green because nothing
    /// has been checked for a day is worse than one that says so.
    /// </summary>
    Task<ReportDiagnostics> QueryDiagnosticsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The active incidents on the same filtered, visible monitors, newest first.
    /// </summary>
    /// <remarks>
    /// It is here rather than on the incident reader so the list and the summary's incident
    /// count are drawn from one selection. Reading the list through a separate filter is exactly
    /// how a dashboard ends up showing a count for one dataset and rows from another.
    /// </remarks>
    Task<IReadOnlyList<ReportIncidentItem>> QueryActiveIncidentsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record ReportDataset(
    ReportQuery Query,
    ReportSummary Summary,
    IReadOnlyList<ReportRow> Rows,
    IReadOnlyList<ReportTrendPoint> Trend,
    int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Query.PageSize));
}

/// <summary>
/// The whole filtered set. There is no truncation flag: a filter too wide to export is refused
/// when it is selected, so a <see cref="ReportExport" /> is always complete for its query.
/// </summary>
public sealed record ReportExport(
    ReportQuery Query,
    IReadOnlyList<ReportRow> Rows,
    int TotalCount);

/// <summary>
/// The dashboard cards. Included and excluded sample counts are both reported, because BR-U02
/// requires an operator to be able to see what the uptime figure left out rather than having to
/// infer it.
/// </summary>
public sealed record ReportSummary(
    int MonitorCount,
    int EndpointCount,
    int HealthyMonitorCount,
    int WarningMonitorCount,
    int CriticalMonitorCount,
    int UnknownMonitorCount,
    int ActiveIncidentCount,
    ReportUptime Uptime,
    ReportResponseTimes ResponseTimes,
    ComparabilityAssessment Comparability);

/// <summary>
/// BR-U01–BR-U03. Every sample category is named for what it is rather than derived from what it
/// is not, so a rule change can never quietly move a sample between them.
/// </summary>
/// <param name="EligibleSamples">
/// The denominator: scheduled availability checks that produced a result outside maintenance.
/// Manual runs, maintenance-suppressed runs, cancelled runs and certificate checks are excluded
/// by <c>counts_for_uptime</c> at write time.
/// </param>
/// <param name="HealthySamples">Eligible samples whose outcome was <c>Healthy</c>.</param>
/// <param name="WarningSamples">
/// Eligible samples that answered but carried a warning finding — a slow response, an oversized
/// page. Reported separately because they are neither uptime nor downtime: the endpoint was
/// reachable, and something about the answer was still worth flagging.
/// </param>
/// <param name="DownSamples">Eligible samples whose outcome was <c>Critical</c>.</param>
/// <param name="ExcludedSamples">Results in the window that were not eligible at all.</param>
public sealed record ReportUptime(
    long EligibleSamples,
    long HealthySamples,
    long WarningSamples,
    long DownSamples,
    long ExcludedSamples)
{
    /// <summary>
    /// BR-U01 as written: healthy eligible samples over eligible samples. A warning sample is
    /// not healthy, so it does not raise this figure; it is visible in
    /// <see cref="WarningSamples" /> instead of being folded into either side.
    /// </summary>
    public double? Percentage => EligibleSamples == 0
        ? null
        : Math.Round(HealthySamples * 100d / EligibleSamples, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Eligible samples where the endpoint answered at all, healthy or warning. It is the
    /// reachability figure some operators mean by "uptime", reported beside the BR-U01 one
    /// rather than instead of it so neither reading has to be inferred.
    /// </summary>
    public double? ReachablePercentage => EligibleSamples == 0
        ? null
        : Math.Round((HealthySamples + WarningSamples) * 100d / EligibleSamples, 4, MidpointRounding.AwayFromZero);
}

/// <summary>
/// BR-U05. Measured over eligible samples that produced a response — healthy or warning — since
/// a warning sample's duration is a real measurement and a failed exchange's is not.
/// <paramref name="MeasuredSamples" /> is that denominator, so a percentile is never read
/// without knowing how many samples produced it. Null when the window held none.
/// </summary>
public sealed record ReportResponseTimes(double? P50Ms, double? P95Ms, long MeasuredSamples);

public sealed record ReportRow(
    Guid EndpointMonitorId,
    Guid EndpointId,
    string ClientName,
    string WebsiteName,
    string EnvironmentName,
    bool IsProduction,
    string EndpointDisplayUrl,
    string MonitorType,
    string OwnerName,
    string ConfirmedStatus,
    /// <summary>
    /// The state a disabled monitor was in when checking stopped, and null for a monitor that is
    /// still running. It exists so the row can say "Disabled · was Healthy" rather than losing
    /// what the endpoint looked like the last time anything actually looked at it.
    /// </summary>
    string? StatusBeforeDisabled,
    DateTimeOffset? ConfirmedAt,
    ReportUptime Uptime,
    ReportResponseTimes ResponseTimes,
    DateTimeOffset? LastMeasuredAt,
    int ActiveIncidentCount,
    string? MonitorSource);

public sealed record ReportTrendPoint(
    DateOnly Day,
    long EligibleSamples,
    long HealthySamples,
    double? UptimePercentage,
    double? P50Ms,
    double? P95Ms);

public sealed record ReportIncidentItem(
    Guid Id,
    Guid EndpointId,
    string EndpointDisplayUrl,
    string ClientName,
    string EnvironmentName,
    string Severity,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? AcknowledgedAt,
    string OwnerName);

/// <summary>
/// BR-C04 across the filtered set. <paramref name="InvalidCount" /> is deliberately separate
/// from <paramref name="HealthyCount" />: a hostname-mismatched or untrusted certificate has no
/// expiry band to report, but it is emphatically not healthy, and folding the two together would
/// let a broken certificate count toward the reassuring number.
/// </summary>
public sealed record ReportCertificateExpiry(
    int NotApplicableCount,
    int UnknownCount,
    int HealthyCount,
    int InvalidCount,
    int WarningCount,
    int HighCount,
    int CriticalCount,
    IReadOnlyList<CertificateExpiryItem> NeedingAttention)
{
    public static ReportCertificateExpiry Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, []);

    /// <summary>Certificates in an expiry band or otherwise invalid.</summary>
    public int AttentionCount => WarningCount + HighCount + CriticalCount + InvalidCount;
}

/// <summary>
/// <paramref name="IsValid" /> is the certificate's validation state; <paramref name="Severity" />
/// is its expiry band. An expired certificate is both invalid and critical. A certificate that
/// is invalid for some other reason has no meaningful band, so its severity is
/// <see cref="CertificateExpirySeverity.None" /> and its invalidity is what has to be reported.
/// </summary>
public sealed record CertificateExpiryItem(
    Guid EndpointId,
    string EndpointDisplayUrl,
    string ClientName,
    string EnvironmentName,
    DateTimeOffset NotAfter,
    int DaysRemaining,
    string ValidationCategory,
    bool IsValid,
    CertificateExpirySeverity Severity,
    DateTimeOffset ObservedAt);

/// <summary>
/// <paramref name="OverdueMonitorCount" /> counts monitors whose next due slot passed more than
/// a grace period ago: a monitor is not late the moment its slot arrives, only once the
/// scheduler has demonstrably failed to pick it up.
/// </summary>
/// <param name="ManualOnlyMonitorCount">
/// Monitors that are enabled but have scheduling switched off, so they run only when someone asks.
/// They belong to neither of the counts above, and without a line of their own the panel accounts
/// for fewer monitors than the view holds — leaving a reader to wonder which ones went missing.
/// </param>
public sealed record ReportDiagnostics(
    int ScheduledMonitorCount,
    int PausedMonitorCount,
    int ManualOnlyMonitorCount,
    int OverdueMonitorCount,
    int WorkInFlightCount,
    int FailedWorkCount,
    DateTimeOffset? LastCompletedCheckAt)
{
    public static ReportDiagnostics Empty { get; } = new(0, 0, 0, 0, 0, 0, null);
}
