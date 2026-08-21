using System.Globalization;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Reporting;
using WebHealth.Domain.Health;

namespace WebHealth.Web.Models;

/// <summary>
/// Turns a monitor type into the words a reader uses for it. The stored values
/// (<c>HttpAvailability</c>, <c>SslCertificate</c>) are stable identifiers and stay that way in
/// the filter, the export and the API; only what the screen prints changes.
/// </summary>
public static class MonitorTypeDisplay
{
    public static string Describe(string? monitorType) => monitorType switch
    {
        HttpIssueIdentity.MonitorType => "Availability",
        SslMonitorIdentity.MonitorType => "SSL certificate",
        null or "" => "—",
        _ => monitorType
    };
}

/// <summary>
/// How long ago something happened, in the one unit that carries the most meaning.
/// </summary>
/// <remarks>
/// Measured against the dataset's own as-of instant rather than the clock, so every relative
/// phrase on the page agrees with the "as of" the summary discloses. A page that says an incident
/// opened "18 minutes ago" while its figures were read an hour earlier is quietly lying about
/// both.
/// </remarks>
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

    /// <summary>
    /// The same phrase with the "ago" that turns a duration into a past instant, except where
    /// the phrase is already one. Callers were appending " ago" themselves, which read as
    /// "Updated just now ago" for anything under a minute old.
    /// </summary>
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

/// <summary>
/// One endpoint's monitors folded into a single row.
/// </summary>
/// <remarks>
/// The reporting layer is monitor-centric, which is correct — a monitor is what holds a status, a
/// schedule and an incident. But it means an HTTPS endpoint occupies two table rows that repeat
/// its client, website, environment and owner, and each row leaves the other's columns holding
/// dashes. Grouping happens here, in the view model, so the shared query, the pager and the CSV
/// export all keep answering in monitors (AC-11) while the screen reads in endpoints.
/// </remarks>
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
    /// <summary>The availability monitor's confirmed state, or null when the endpoint has none.</summary>
    public ReportRow? Availability =>
        Monitors.FirstOrDefault(row => row.MonitorType == HttpIssueIdentity.MonitorType);

    /// <summary>The certificate monitor's confirmed state, or null for a plain HTTP endpoint.</summary>
    public ReportRow? Certificate =>
        Monitors.FirstOrDefault(row => row.MonitorType == SslMonitorIdentity.MonitorType);

    public int ActiveIncidentCount => Monitors.Sum(row => row.ActiveIncidentCount);

    /// <summary>
    /// The most recent check across the endpoint's monitors. The endpoint is as freshly measured
    /// as its newest reading, so taking the earliest would report it as staler than it is.
    /// </summary>
    public DateTimeOffset? LastMeasuredAt =>
        Monitors.Where(row => row.LastMeasuredAt is not null).Max(row => row.LastMeasuredAt);

    /// <summary>
    /// The worst state any of the endpoint's monitors is in, which is what decides whether the
    /// row needs attention. Ordering is by severity, not alphabetical.
    /// </summary>
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

    /// <summary>
    /// Groups one page of monitor rows by endpoint, preserving the order the reporting layer
    /// returned them in.
    /// </summary>
    /// <remarks>
    /// That ordering — client, website, environment, URL, then monitor type — already places an
    /// endpoint's monitors next to each other, so grouping never reorders the page. An endpoint
    /// whose monitors straddle a page boundary appears on both pages holding the monitors that
    /// page carries, which is why the pager and the count chip keep speaking in monitors.
    /// </remarks>
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
