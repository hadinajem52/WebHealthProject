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
    /// <summary>
    /// Monitors that are switched off. Counted separately so the four health chips plus this one
    /// account for every monitor in <see cref="MonitorCount" />: folding them into Unknown would
    /// say "not yet checked" about an endpoint that will never be checked again.
    /// </summary>
    int DisabledMonitorCount,
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
/// <param name="HealthySamples">
/// Eligible samples that answered and carried no finding at all. The strictest reading: nothing
/// about the response was worth reporting.
/// </param>
/// <param name="WarningSamples">
/// Eligible samples where the endpoint answered but something about the answer was worth
/// flagging — a slow response, an oversized page, a robots or canonical rule. These count as
/// <em>up</em>: the visitor got the page. They are still counted apart from
/// <see cref="HealthySamples" /> so "up" and "flawless" never have to be inferred from one
/// number.
/// </param>
/// <param name="DownSamples">
/// Eligible samples with an availability failure — DNS, connection, TLS, timeout, an HTTP error
/// status, a content mismatch, a redirect fault. See <c>UptimeParticipation</c> for the
/// categories that do and do not land here.
/// </param>
/// <param name="ExcludedSamples">Results in the window that were not eligible at all.</param>
public sealed record ReportUptime(
    long EligibleSamples,
    long HealthySamples,
    long WarningSamples,
    long DownSamples,
    long ExcludedSamples)
{
    /// <summary>
    /// BR-U01: healthy <em>availability</em> samples over eligible <em>availability</em>
    /// samples. Every sample where the endpoint answered counts, whether or not the answer
    /// satisfied every rule, because that is what availability means — see
    /// <c>UptimeParticipation</c> for why a robots or canonical fault is not downtime.
    /// </summary>
    public double? Percentage => EligibleSamples == 0
        ? null
        : Math.Round(
            (HealthySamples + WarningSamples) * 100d / EligibleSamples, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The stricter figure: eligible samples that answered with nothing to report at all. It sits
    /// beside <see cref="Percentage" /> rather than replacing it because the gap between the two
    /// is the interesting part — an endpoint at 100% uptime and 12% clean is up and misconfigured,
    /// which no single number can say.
    /// </summary>
    public double? CleanPercentage => EligibleSamples == 0
        ? null
        : Math.Round(HealthySamples * 100d / EligibleSamples, 4, MidpointRounding.AwayFromZero);
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
    /// <summary>Eligible samples where the endpoint answered, matching the summary's uptime.</summary>
    long UpSamples,
    double? UptimePercentage,
    double? P50Ms,
    double? P95Ms);

/// <summary>
/// An active incident as the dashboard lists it.
/// </summary>
/// <param name="IssueKey">
/// The deduplication identity, carried so the list can say <em>what failed</em> rather than only
/// where and how badly. Triage starts from the failure, and an incident row without one asks the
/// reader to open the detail page before they can tell a DNS outage from a slow response.
/// </param>
/// <param name="MonitorType">
/// Which monitor confirmed the failure. It separates an availability incident from a certificate
/// one, which the severity and the endpoint alone cannot.
/// </param>
public sealed record ReportIncidentItem(
    Guid Id,
    Guid EndpointId,
    string EndpointDisplayUrl,
    string ClientName,
    string EnvironmentName,
    string IssueKey,
    string MonitorType,
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
