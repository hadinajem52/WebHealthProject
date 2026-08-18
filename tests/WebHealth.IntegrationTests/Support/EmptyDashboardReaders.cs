using WebHealth.Application.Incidents;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Web.Models;

namespace WebHealth.IntegrationTests.Support;

/// <summary>
/// The dashboard is now a real read surface, so the shell tests — which run with no database —
/// stub the readers behind it exactly as they already stub the notification feed. These tests
/// are about the shell's landmarks, encoding and asset references; the dashboard's data is
/// covered by the reporting query core's own evidence against a real cluster.
/// </summary>
internal static class EmptyDashboard
{
    public static ReportQuery Query(DateTimeOffset asOf) =>
        ReportQueryNormalizer.Normalize(new ReportQueryInput(), ReportMonitorTypes.All, asOf).Query!;

    public static ReportDataset Dataset(DateTimeOffset asOf) => new(
        Query(asOf),
        new ReportSummary(
            0, 0, 0, 0, 0, 0, 0,
            new ReportUptime(0, 0, 0, 0, 0),
            new ReportResponseTimes(null, null, 0),
            PerformanceComparability.Evaluate([])),
        [],
        [],
        0);

    public static DashboardViewModel ViewModel(DateTimeOffset asOf) => new(
        new DashboardFilterViewModel(),
        DashboardFilterOptions.Empty,
        new FilterSummaryViewModel(asOf, []),
        Dataset(asOf),
        ReportCertificateExpiry.Empty,
        ReportDiagnostics.Empty,
        [],
        []);
}

internal sealed class EmptyReportingReader : IReportingReader
{
    public Task<ReportDataset> QueryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(EmptyDashboard.Dataset(query.WindowEnd) with { Query = query });

    public Task<ReportExport> ExportAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReportExport(query, [], 0));

    public Task<ReportCertificateExpiry> QueryCertificateExpiryAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ReportCertificateExpiry.Empty);

    public Task<ReportDiagnostics> QueryDiagnosticsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ReportDiagnostics.Empty);

    public Task<IReadOnlyList<ReportIncidentItem>> QueryActiveIncidentsAsync(
        ReportQuery query,
        RegistryAccessContext access,
        int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReportIncidentItem>>([]);
}

internal sealed class EmptyRegistryReader : IRegistryReader
{
    public Task<IReadOnlyList<ClientListItem>> ListClientsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClientListItem>>([]);

    public Task<IReadOnlyList<ClientListItem>> ListDeletedClientsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClientListItem>>([]);

    public Task<ClientDetails?> FindClientAsync(
        Guid clientId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ClientDetails?>(null);

    public Task<IReadOnlyList<WebsiteListItem>> ListWebsitesAsync(
        RegistryAccessContext access,
        Guid? tagId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WebsiteListItem>>([]);

    public Task<IReadOnlyList<RegistryTagOption>> ListTagsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RegistryTagOption>>([]);

    public Task<IReadOnlyList<WebsiteListItem>> ListDeletedWebsitesAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WebsiteListItem>>([]);

    public Task<WebsiteDetails?> FindWebsiteAsync(
        Guid websiteId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<WebsiteDetails?>(null);

    public Task<IReadOnlyList<RegistryOwnerOption>> ListOwnersAsync(
        Guid? includeOwnerSubjectId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RegistryOwnerOption>>([]);
}

internal sealed class EmptyIncidentReader : IIncidentReader
{
    public Task<IncidentListPage> ListAsync(
        IncidentListFilter filter,
        RegistryAccessContext access,
        int page = 1,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new IncidentListPage([], 1, 25, 0));

    public Task<IncidentDetails?> FindAsync(
        Guid incidentId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IncidentDetails?>(null);
}

internal sealed class EmptyTargetRegistryReader : ITargetRegistryReader
{
    public Task<IReadOnlyList<EnvironmentListItem>> ListEnvironmentsAsync(
        Guid websiteId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EnvironmentListItem>>([]);

    public Task<IReadOnlyList<EnvironmentListItem>> ListAllEnvironmentsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EnvironmentListItem>>([]);

    public Task<EnvironmentDetails?> FindEnvironmentAsync(
        Guid environmentId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<EnvironmentDetails?>(null);

    public Task<IReadOnlyList<EndpointListItem>> ListEndpointsAsync(
        Guid environmentId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EndpointListItem>>([]);

    public Task<IReadOnlyList<RegistryEndpointItem>> ListAllEndpointsAsync(
        RegistryAccessContext access,
        string? search = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RegistryEndpointItem>>([]);

    public Task<EndpointDetails?> FindEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<EndpointDetails?>(null);

    public Task<CertificateStatus?> FindCertificateStatusAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CertificateStatus?>(null);

    public Task<IReadOnlyList<EnvironmentListItem>> ListDeletedEnvironmentsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EnvironmentListItem>>([]);

    public Task<IReadOnlyList<EndpointListItem>> ListDeletedEndpointsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EndpointListItem>>([]);
}
