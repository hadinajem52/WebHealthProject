using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Reporting;

public interface IReportingReader
{
    Task<ReportDataset> QueryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<ReportExport> ExportAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<ReportCertificateExpiry> QueryCertificateExpiryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

    Task<ReportDiagnostics> QueryDiagnosticsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);

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

public sealed record ReportExport(
    ReportQuery Query,
    IReadOnlyList<ReportRow> Rows,
    int TotalCount);

public sealed record ReportSummary(
    int MonitorCount,
    int EndpointCount,
    int HealthyMonitorCount,
    int WarningMonitorCount,
    int CriticalMonitorCount,
    int UnknownMonitorCount,
    int DisabledMonitorCount,
    int ActiveIncidentCount,
    ReportUptime Uptime,
    ReportResponseTimes ResponseTimes,
    ComparabilityAssessment Comparability);

public sealed record ReportUptime(
    long EligibleSamples,
    long HealthySamples,
    long WarningSamples,
    long DownSamples,
    long ExcludedSamples)
{
    public double? Percentage => EligibleSamples == 0
        ? null
        : Math.Round(
            (HealthySamples + WarningSamples) * 100d / EligibleSamples, 4, MidpointRounding.AwayFromZero);

    public double? CleanPercentage => EligibleSamples == 0
        ? null
        : Math.Round(HealthySamples * 100d / EligibleSamples, 4, MidpointRounding.AwayFromZero);
}

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
    long UpSamples,
    double? UptimePercentage,
    double? P50Ms,
    double? P95Ms);

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

    public int AttentionCount => WarningCount + HighCount + CriticalCount + InvalidCount;
}

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
