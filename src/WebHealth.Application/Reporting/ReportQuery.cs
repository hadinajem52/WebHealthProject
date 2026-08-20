using WebHealth.Application.Monitoring;
using WebHealth.Domain.Health;

namespace WebHealth.Application.Reporting;

/// <summary>
/// The monitor types a report may be filtered to. Reports name them in one place so a new
/// monitor type cannot be added to the pipeline and silently stay unreportable.
/// </summary>
public static class ReportMonitorTypes
{
    public static IReadOnlyList<string> All { get; } =
        [HttpIssueIdentity.MonitorType, SslMonitorIdentity.MonitorType];
}

/// <summary>
/// The one filter object every reporting surface uses. Dashboard cards, the endpoint table, the
/// trend chart and the CSV export are all produced from a single <see cref="ReportQuery" /> by a
/// single query layer, which is what makes AC-11 — "dashboard filters and CSV export return the
/// same logical dataset" — true by construction rather than by two implementations agreeing.
/// </summary>
/// <remarks>
/// Instances are only ever produced by <see cref="ReportQueryNormalizer" />, and every way of
/// deriving one re-applies the same paging bounds. A caller cannot obtain a query with an
/// unbounded window, a zero page size or a negative page, so the server-side bounds are an
/// invariant of the type rather than a convention each caller has to honour.
/// </remarks>
public sealed record ReportQuery
{
    internal ReportQuery(
        Guid? clientId,
        Guid? websiteId,
        Guid? environmentId,
        Guid? ownerSubjectId,
        string? healthStatus,
        string? monitorType,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int page,
        int pageSize)
    {
        ClientId = clientId;
        WebsiteId = websiteId;
        EnvironmentId = environmentId;
        OwnerSubjectId = ownerSubjectId;
        HealthStatus = healthStatus;
        MonitorType = monitorType;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        Page = ReportQueryNormalizer.BoundPage(page);
        PageSize = ReportQueryNormalizer.BoundPageSize(pageSize);
    }

    public Guid? ClientId { get; }
    public Guid? WebsiteId { get; }
    public Guid? EnvironmentId { get; }
    public Guid? OwnerSubjectId { get; }
    public string? HealthStatus { get; }
    public string? MonitorType { get; }

    /// <summary>Inclusive lower bound of the reporting window, in UTC (BR-U04).</summary>
    public DateTimeOffset WindowStart { get; }

    /// <summary>
    /// Exclusive upper bound of the reporting window, in UTC. A sample measured exactly at this
    /// instant belongs to the next period, never to this one, so adjacent periods can never
    /// double-count a check (BR-U04).
    /// </summary>
    public DateTimeOffset WindowEnd { get; }

    public int Page { get; }
    public int PageSize { get; }

    /// <summary>
    /// The same filter on a different page. The page and page size are re-bounded here, so this
    /// cannot be used to escape the limits the normalizer applied.
    /// </summary>
    public ReportQuery WithPaging(int page, int? pageSize = null) => new(
        ClientId, WebsiteId, EnvironmentId, OwnerSubjectId, HealthStatus, MonitorType,
        WindowStart, WindowEnd, page, pageSize ?? PageSize);

    /// <summary>
    /// The same filter, sliced for export.
    /// </summary>
    /// <remarks>
    /// The export is <em>the whole filtered set</em>, not the page the screen happens to be on:
    /// a file that silently contained only rows 26–50 would be read as the complete answer. Its
    /// page size is the same bound the query layer refuses to exceed when selecting monitors, so
    /// an export can never be a truncated file — a filter too wide to export is refused outright
    /// rather than served as an apparently complete one.
    /// </remarks>
    public ReportQuery ForExport() => WithPaging(1, ReportQueryNormalizer.MaximumMonitors);
}

/// <summary>Unvalidated input, exactly as it arrives from a query string or form post.</summary>
public sealed record ReportQueryInput(
    Guid? ClientId = null,
    Guid? WebsiteId = null,
    Guid? EnvironmentId = null,
    Guid? OwnerSubjectId = null,
    string? HealthStatus = null,
    string? MonitorType = null,
    DateTimeOffset? WindowStart = null,
    DateTimeOffset? WindowEnd = null,
    int Page = 1);

public sealed record ReportQueryResult(ReportQuery? Query, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Query is not null;
}

public static class ReportQueryNormalizer
{
    public const int ScreenPageSize = 25;

    /// <summary>
    /// How many monitors one report may cover. It bounds the selection, the identifier array the
    /// aggregates receive, and the export's page size — one number, so an export can never
    /// contain fewer monitors than the filter selected.
    /// </summary>
    public const int MaximumMonitors = 5_000;

    public const int DefaultWindowDays = 30;
    public const int MaximumWindowDays = 366;

    public static IReadOnlyList<string> SelectableHealthStatuses { get; } =
    [
        EndpointHealthStatuses.Healthy,
        EndpointHealthStatuses.Warning,
        EndpointHealthStatuses.Critical,
        EndpointHealthStatuses.Unknown,
        // Selectable because it is now a status the dashboard reports: without it, filtering for
        // the monitors nobody is checking was a query that could only ever return nothing.
        EndpointHealthStatuses.Disabled
    ];

    internal static int BoundPage(int page) => Math.Max(page, 1);

    internal static int BoundPageSize(int pageSize) => Math.Clamp(pageSize, 1, MaximumMonitors);

    /// <summary>
    /// Validates and bounds the request. Every bound is applied here, server-side, so a crafted
    /// request cannot ask for a five-year window or page one million.
    /// </summary>
    public static ReportQueryResult Normalize(
        ReportQueryInput input,
        IReadOnlyCollection<string> selectableMonitorTypes,
        DateTimeOffset now)
    {
        var errors = new List<string>();

        // The window is resolved in UTC before anything is compared, so a request carrying a
        // local offset selects the same instants as one carrying Z.
        var end = (input.WindowEnd ?? now).ToUniversalTime();
        var start = (input.WindowStart ?? end.AddDays(-DefaultWindowDays)).ToUniversalTime();

        if (end <= start)
        {
            errors.Add("The report window must end after it starts.");
        }
        else if (end - start > TimeSpan.FromDays(MaximumWindowDays))
        {
            errors.Add($"The report window cannot be longer than {MaximumWindowDays} days.");
        }

        var status = Trimmed(input.HealthStatus);
        if (status is not null && !SelectableHealthStatuses.Contains(status, StringComparer.Ordinal))
        {
            errors.Add("Select a valid health status.");
        }

        var monitorType = Trimmed(input.MonitorType);
        if (monitorType is not null && !selectableMonitorTypes.Contains(monitorType, StringComparer.Ordinal))
        {
            errors.Add("Select a valid monitor type.");
        }

        if (errors.Count > 0)
        {
            return new(null, errors);
        }

        return new(
            new ReportQuery(
                input.ClientId,
                input.WebsiteId,
                input.EnvironmentId,
                input.OwnerSubjectId,
                status,
                monitorType,
                start,
                end,
                input.Page,
                ScreenPageSize),
            []);
    }

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// Thrown when a filter covers more monitors than one report may aggregate over. It is a request
/// to narrow the filter, not a failure: serving it would mean an unbounded query behind a single
/// page load, and exporting it would mean a file that silently omitted part of its own answer.
/// </summary>
/// <remarks>
/// It lives in the application layer, beside the contract it belongs to, so a web controller can
/// handle it without taking a dependency on the infrastructure assembly that raises it.
/// </remarks>
public sealed class ReportTooLargeException(int maximumMonitors)
    : InvalidOperationException(
        $"This filter covers more than {maximumMonitors} monitors. Narrow it by client, "
        + "website, environment, owner or monitor type.")
{
    public int MaximumMonitors { get; } = maximumMonitors;
}
