using WebHealth.Application.Monitoring;
using WebHealth.Domain.Health;

namespace WebHealth.Application.Reporting;

public static class ReportMonitorTypes
{
    public static IReadOnlyList<string> All { get; } =
        [HttpIssueIdentity.MonitorType, SslMonitorIdentity.MonitorType];
}

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

    public DateTimeOffset WindowStart { get; }

    public DateTimeOffset WindowEnd { get; }

    public int Page { get; }
    public int PageSize { get; }

    public ReportQuery WithPaging(int page, int? pageSize = null) => new(
        ClientId, WebsiteId, EnvironmentId, OwnerSubjectId, HealthStatus, MonitorType,
        WindowStart, WindowEnd, page, pageSize ?? PageSize);

    public ReportQuery ForExport() => WithPaging(1, ReportQueryNormalizer.MaximumMonitors);
}

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

    public const int MaximumMonitors = 5_000;

    public const int DefaultWindowDays = 30;
    public const int MaximumWindowDays = 366;

    public static IReadOnlyList<string> SelectableHealthStatuses { get; } =
    [
        EndpointHealthStatuses.Healthy,
        EndpointHealthStatuses.Warning,
        EndpointHealthStatuses.Critical,
        EndpointHealthStatuses.Unknown,
        EndpointHealthStatuses.Disabled
    ];

    internal static int BoundPage(int page) => Math.Max(page, 1);

    internal static int BoundPageSize(int pageSize) => Math.Clamp(pageSize, 1, MaximumMonitors);

    public static ReportQueryResult Normalize(
        ReportQueryInput input,
        IReadOnlyCollection<string> selectableMonitorTypes,
        DateTimeOffset now)
    {
        var errors = new List<string>();

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

public sealed class ReportTooLargeException(int maximumMonitors)
    : InvalidOperationException(
        $"This filter covers more than {maximumMonitors} monitors. Narrow it by client, "
        + "website, environment, owner or monitor type.")
{
    public int MaximumMonitors { get; } = maximumMonitors;
}
