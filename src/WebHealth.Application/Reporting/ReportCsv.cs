using System.Globalization;

namespace WebHealth.Application.Reporting;

public static class ReportCsv
{
    public static IReadOnlyList<string> Headers { get; } =
    [
        "EndpointMonitorId",
        "EndpointId",
        "Client",
        "Website",
        "Environment",
        "IsProduction",
        "EndpointUrl",
        "MonitorType",
        "Owner",
        "ConfirmedStatus",
        "ConfirmedAt",
        "EligibleSamples",
        "HealthySamples",
        "WarningSamples",
        "DownSamples",
        "ExcludedSamples",
        "UptimePercent",
        "CleanPercent",
        "P50Ms",
        "P95Ms",
        "MeasuredSamples",
        "LastMeasuredAt",
        "ActiveIncidents",
        "MonitorSource",
        "OperationalState",
        "LastScheduledCompletionAt",
        "HistoryMode",
        "RawSamples",
        "AggregatedSamples",
        "PercentileMethod",
        "PartialArchivedDaysOmitted"
    ];

    public static byte[] Write(ReportExport export) =>
        CsvWriter.Write(Headers, export.Rows.Select(ToFields));

    public static string FileName(ReportQuery query) => string.Format(
        CultureInfo.InvariantCulture,
        "webhealth-report-{0:yyyyMMdd}-{1:yyyyMMdd}.csv",
        query.WindowStart,
        query.WindowEnd);

    private static IReadOnlyList<CsvField> ToFields(ReportRow row) =>
    [
        CsvField.Token(row.EndpointMonitorId.ToString()),
        CsvField.Token(row.EndpointId.ToString()),
        CsvField.Text(row.ClientName),
        CsvField.Text(row.WebsiteName),
        CsvField.Text(row.EnvironmentName),
        CsvField.Flag(row.IsProduction),
        CsvField.Text(row.EndpointDisplayUrl),
        CsvField.Token(row.MonitorType),
        CsvField.Text(row.OwnerName),
        CsvField.Token(row.ConfirmedStatus),
        CsvField.Timestamp(row.ConfirmedAt),
        CsvField.Count(row.Uptime.EligibleSamples),
        CsvField.Count(row.Uptime.HealthySamples),
        CsvField.Count(row.Uptime.WarningSamples),
        CsvField.Count(row.Uptime.DownSamples),
        CsvField.Count(row.Uptime.ExcludedSamples),
        CsvField.Number(row.Uptime.Percentage),
        CsvField.Number(row.Uptime.CleanPercentage),
        CsvField.Number(row.ResponseTimes.P50Ms),
        CsvField.Number(row.ResponseTimes.P95Ms),
        CsvField.Count(row.ResponseTimes.MeasuredSamples),
        CsvField.Timestamp(row.LastMeasuredAt),
        CsvField.Count(row.ActiveIncidentCount),
        CsvField.Token(row.MonitorSource),
        CsvField.Token(row.Operation?.State),
        CsvField.Timestamp(row.Operation?.LastScheduledCompletionAt),
        CsvField.Token(row.History?.Mode ?? "Raw"),
        CsvField.Count(row.History?.RawSamples ?? row.Uptime.EligibleSamples + row.Uptime.ExcludedSamples),
        CsvField.Count(row.History?.AggregatedSamples ?? 0),
        CsvField.Token(row.ResponseTimes.IsApproximate ? "ApproximateHistogram" : "ExactRaw"),
        CsvField.Flag(row.History?.PartialArchivedDaysOmitted ?? false)
    ];
}
