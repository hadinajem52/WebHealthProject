using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;

namespace WebHealth.Web.Models;

/// <summary>
/// The dashboard filter, bound from the query string so a filtered dashboard is a shareable URL
/// and the CSV export can be reached with the identical query.
/// </summary>
/// <remarks>
/// It carries every dimension <see cref="ReportQuery" /> supports. A filter the query layer
/// applies but the primary screen cannot express would leave part of the shared contract
/// unreachable — and would make the export link unable to represent what the user is looking at.
/// </remarks>
public sealed class DashboardFilterViewModel
{
    [Display(Name = "Client")]
    public Guid? ClientId { get; set; }

    [Display(Name = "Website")]
    public Guid? WebsiteId { get; set; }

    [Display(Name = "Environment")]
    public Guid? EnvironmentId { get; set; }

    [Display(Name = "Owner")]
    public Guid? OwnerSubjectId { get; set; }

    [Display(Name = "Health status")]
    public string? HealthStatus { get; set; }

    [Display(Name = "Monitor type")]
    public string? MonitorType { get; set; }

    [DataType(DataType.Date), Display(Name = "From (UTC)")]
    public DateTime? WindowStart { get; set; }

    [DataType(DataType.Date), Display(Name = "To (UTC)")]
    public DateTime? WindowEnd { get; set; }

    public int Page { get; set; } = 1;

    public ReportQueryInput ToInput() => new(
        ClientId,
        WebsiteId,
        EnvironmentId,
        OwnerSubjectId,
        HealthStatus,
        MonitorType,
        WindowStart is { } start ? new DateTimeOffset(start.Date, TimeSpan.Zero) : null,
        WindowEnd is { } end ? new DateTimeOffset(end.Date, TimeSpan.Zero) : null,
        Page);

    /// <summary>
    /// The same filter as route values, so the export link and the pager cannot drift from what
    /// the page is showing. Every dimension is here for the same reason it is on the form.
    /// </summary>
    public Dictionary<string, string?> ToRouteValues() => new()
    {
        ["ClientId"] = ClientId?.ToString(),
        ["WebsiteId"] = WebsiteId?.ToString(),
        ["EnvironmentId"] = EnvironmentId?.ToString(),
        ["OwnerSubjectId"] = OwnerSubjectId?.ToString(),
        ["HealthStatus"] = HealthStatus,
        ["MonitorType"] = MonitorType,
        ["WindowStart"] = WindowStart?.ToString("yyyy-MM-dd"),
        ["WindowEnd"] = WindowEnd?.ToString("yyyy-MM-dd")
    };
}

/// <summary>The lookup lists the filter's selects are populated from.</summary>
public sealed record DashboardFilterOptions(
    IReadOnlyList<ClientListItem> Clients,
    IReadOnlyList<WebsiteListItem> Websites,
    IReadOnlyList<EnvironmentListItem> Environments,
    IReadOnlyList<RegistryOwnerOption> Owners)
{
    public static DashboardFilterOptions Empty { get; } = new([], [], [], []);
}

public sealed record DashboardViewModel(
    DashboardFilterViewModel Filter,
    DashboardFilterOptions Options,
    FilterSummaryViewModel Summary,
    ReportDataset Dataset,
    ReportCertificateExpiry Certificates,
    ReportDiagnostics Diagnostics,
    IReadOnlyList<ReportIncidentItem> ActiveIncidents,
    IReadOnlyList<string> Errors)
{
    public bool HasFilterErrors => Errors.Count > 0;
}
