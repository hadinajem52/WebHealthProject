using System.Globalization;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Reporting;
using WebHealth.Domain.Health;

namespace WebHealth.Web.Models;

public static class MonitorTypeDisplay
{
    public static string Describe(string? monitorType) => monitorType switch
    {
        HttpIssueIdentity.MonitorType => "Availability",
        SslMonitorIdentity.MonitorType => "SSL certificate",
        null or "" => "—",
        _ => monitorType
    };

    public static string DescribeFromMonitorSource(string? monitorSource) => monitorSource switch
    {
        HttpResultNormalizer.MonitorSource => "Availability",
        SslResultNormalizer.MonitorSource => "SSL certificate",
        null or "" => "—",
        _ => monitorSource
    };
}

public static class RelativeTime
{
    public static string Describe(DateTimeOffset value, DateTimeOffset asOf)
    {
        var elapsed = asOf - value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return JustNow;
        }

        if (elapsed.TotalHours < 1)
        {
            return Plural((int)elapsed.TotalMinutes, "minute");
        }

        if (elapsed.TotalDays < 1)
        {
            return Plural((int)elapsed.TotalHours, "hour");
        }

        return Plural((int)elapsed.TotalDays, "day");
    }

    public static string DescribeAgo(DateTimeOffset value, DateTimeOffset asOf)
    {
        var described = Describe(value, asOf);
        return described == JustNow ? described : $"{described} ago";
    }

    private const string JustNow = "just now";

    private static string Plural(int count, string unit) =>
        count == 1
            ? $"1 {unit}"
            : string.Create(CultureInfo.InvariantCulture, $"{count} {unit}s");
}

public sealed record EndpointHealthGroup(
    Guid EndpointId,
    string EndpointDisplayUrl,
    string ClientName,
    string WebsiteName,
    string EnvironmentName,
    bool IsProduction,
    string OwnerName,
    IReadOnlyList<ReportRow> Monitors)
{
    public ReportRow? Availability =>
        Monitors.FirstOrDefault(row => row.MonitorType == HttpIssueIdentity.MonitorType);

    public ReportRow? Certificate =>
        Monitors.FirstOrDefault(row => row.MonitorType == SslMonitorIdentity.MonitorType);

    public int ActiveIncidentCount => Monitors.Sum(row => row.ActiveIncidentCount);

    public DateTimeOffset? LastMeasuredAt =>
        Monitors.Where(row => row.LastMeasuredAt is not null).Max(row => row.LastMeasuredAt);

    public string WorstStatus
    {
        get
        {
            var ranked = Monitors
                .Select(row => row.ConfirmedStatus)
                .OrderByDescending(Rank)
                .ThenBy(status => status, StringComparer.Ordinal)
                .FirstOrDefault();

            return ranked ?? EndpointHealthStatuses.Unknown;
        }
    }

    public bool NeedsAttention => WorstStatus is EndpointHealthStatuses.Critical
        or EndpointHealthStatuses.Warning || ActiveIncidentCount > 0;

    private static int Rank(string status) => status switch
    {
        EndpointHealthStatuses.Critical => 4,
        EndpointHealthStatuses.Warning => 3,
        EndpointHealthStatuses.Unknown => 2,
        EndpointHealthStatuses.Disabled => 1,
        _ => 0
    };

    public static IReadOnlyList<EndpointHealthGroup> From(IReadOnlyList<ReportRow> rows) => rows
        .GroupBy(row => row.EndpointId)
        .Select(group =>
        {
            var first = group.First();
            return new EndpointHealthGroup(
                group.Key,
                first.EndpointDisplayUrl,
                first.ClientName,
                first.WebsiteName,
                first.EnvironmentName,
                first.IsProduction,
                first.OwnerName,
                [.. group]);
        })
        .ToArray();
}
