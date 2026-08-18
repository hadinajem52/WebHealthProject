using System.Globalization;

namespace WebHealth.Application.Reporting;

/// <summary>
/// Renders a <see cref="ReportExport" /> as CSV. It takes the rows the query layer produced and
/// does nothing but format them: there is no second filter, no second sort and no second
/// authorization check here, which is the mechanism behind AC-11. If this file ever grows a
/// <c>Where</c>, the screen and the export have stopped being the same dataset.
/// </summary>
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
        "ReachablePercent",
        "P50Ms",
        "P95Ms",
        "MeasuredSamples",
        "LastMeasuredAt",
        "ActiveIncidents",
        "MonitorSource"
    ];

    public static byte[] Write(ReportExport export) =>
        CsvWriter.Write(Headers, export.Rows.Select(ToFields));

    /// <summary>
    /// A filename that states the window the file covers, so a downloaded export is still
    /// self-describing once it is sitting in someone's downloads folder.
    /// </summary>
    public static string FileName(ReportQuery query) => string.Format(
        CultureInfo.InvariantCulture,
        "webhealth-report-{0:yyyyMMdd}-{1:yyyyMMdd}.csv",
        query.WindowStart,
        query.WindowEnd);

    private static IReadOnlyList<CsvField> ToFields(ReportRow row) =>
    [
        CsvField.Token(row.EndpointMonitorId.ToString()),
        CsvField.Token(row.EndpointId.ToString()),
        // Client, website, environment, URL and owner are all names a user typed, so they are
        // the fields the formula guard exists for.
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
        CsvField.Number(row.Uptime.ReachablePercentage),
        CsvField.Number(row.ResponseTimes.P50Ms),
        CsvField.Number(row.ResponseTimes.P95Ms),
        CsvField.Count(row.ResponseTimes.MeasuredSamples),
        CsvField.Timestamp(row.LastMeasuredAt),
        CsvField.Count(row.ActiveIncidentCount),
        CsvField.Token(row.MonitorSource)
    ];
}
