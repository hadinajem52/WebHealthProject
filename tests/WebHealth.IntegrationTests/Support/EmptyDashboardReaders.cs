using WebHealth.Application.Incidents;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Web.Models;

namespace WebHealth.IntegrationTests.Support;

internal static class EmptyDashboard
{
    public static ReportQuery Query(DateTimeOffset asOf) =>
        ReportQueryNormalizer.Normalize(new ReportQueryInput(), ReportMonitorTypes.All, asOf).Query!;

    public static ReportDataset Dataset(DateTimeOffset asOf) => new(
        Query(asOf),
        new ReportSummary(
            0, 0, 0, 0, 0, 0, 0, 0,
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
    public static WebsiteDetails Website { get; } = new(
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000003"),
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000002"),
        "Example client",
        "Example",
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000006"),
        "Example owner",
        "ASP.NET Core",
        IsEnabled: false,
        IsDeleted: false,
        Version: 1,
        ActiveEnvironmentCount: 1,
        Tags: []);

    private static WebsiteListItem WebsiteItem { get; } = new(
        Website.Id,
        Website.ClientId,
        Website.ClientName,
        Website.Name,
        Website.OwnerName,
        Website.TechnologyCms,
        Website.IsEnabled,
        Website.IsDeleted,
        Website.Version,
        Website.ActiveEnvironmentCount,
        Website.Tags);

    public static ClientDetails Client { get; } = new(
        Website.ClientId,
        Website.ClientName,
        Website.OwnerSubjectId,
        Website.OwnerName,
        "Endpoint-first test client",
        IsActive: true,
        IsDeleted: false,
        Version: 1,
        Websites: [WebsiteItem]);

    private static ClientListItem ClientItem { get; } = new(
        Client.Id,
        Client.Name,
        Client.OwnerName,
        Client.IsActive,
        Client.IsDeleted,
        Client.Version,
        Client.Websites.Count);

    public Task<IReadOnlyList<ClientListItem>> ListClientsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClientListItem>>([ClientItem]);

    public Task<IReadOnlyList<ClientListItem>> ListDeletedClientsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClientListItem>>([]);

    public Task<ClientDetails?> FindClientAsync(
        Guid clientId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ClientDetails?>(clientId == Client.Id ? Client : null);

    public Task<IReadOnlyList<WebsiteListItem>> ListWebsitesAsync(
        RegistryAccessContext access,
        Guid? tagId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WebsiteListItem>>([WebsiteItem]);

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
        Task.FromResult<WebsiteDetails?>(websiteId == Website.Id ? Website : null);

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
    public static EnvironmentDetails Environment { get; } = new(
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000004"),
        EmptyRegistryReader.Website.ClientId,
        EmptyRegistryReader.Website.Id,
        EmptyRegistryReader.Website.Name,
        "Production",
        EnvironmentTypes.Production,
        IsProduction: true,
        "https://example.com/",
        IsActive: true,
        IsDeleted: false,
        Version: 1,
        Endpoints: []);

    private static EnvironmentListItem EnvironmentItem { get; } = new(
        Environment.Id,
        Environment.WebsiteId,
        EmptyRegistryReader.Client.Name,
        Environment.WebsiteName,
        Environment.Name,
        Environment.EnvironmentType,
        Environment.IsProduction,
        Environment.BaseUrl,
        Environment.IsActive,
        Environment.IsDeleted,
        Environment.Version,
        Environment.Endpoints.Count);

    public static EndpointDetails BlockedEndpoint { get; } = new(
        Id: Guid.Parse("6f1c9a20-0000-0000-0000-000000000005"),
        EnvironmentId: Environment.Id,
        EnvironmentName: Environment.Name,
        IsProduction: Environment.IsProduction,
        WebsiteId: EmptyRegistryReader.Website.Id,
        WebsiteName: EmptyRegistryReader.Website.Name,
        DisplayUrl: "https://example.com/health",
        NormalizedUrl: "https://example.com/health",
        NormalizationVersion: 1,
        OwnerSubjectId: EmptyRegistryReader.Website.OwnerSubjectId,
        OwnerName: EmptyRegistryReader.Website.OwnerName,
        InheritsWebsiteOwner: true,
        IsEnabled: true,
        IsDeleted: false,
        HasHttpException: false,
        HttpExceptionReason: null,
        Version: 1,
        MonitorType: "Http",
        IntervalSeconds: 300,
        IntervalMinutesOverride: null,
        WarningThresholdMs: 1500,
        CriticalThresholdMs: 3000,
        HasThresholdOverride: false,
        TimeoutSeconds: 15,
        MonitorEnabled: true,
        SchedulingEnabled: true,
        IsMonitoringEligible: false,
        CanTest: false);

    public Task<IReadOnlyList<EnvironmentListItem>> ListEnvironmentsAsync(
        Guid websiteId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EnvironmentListItem>>(
            websiteId == Environment.WebsiteId ? [EnvironmentItem] : []);

    public Task<IReadOnlyList<EnvironmentListItem>> ListAllEnvironmentsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EnvironmentListItem>>([EnvironmentItem]);

    public Task<EnvironmentDetails?> FindEnvironmentAsync(
        Guid environmentId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<EnvironmentDetails?>(environmentId == Environment.Id ? Environment : null);

    public Task<IReadOnlyList<EndpointListItem>> ListEndpointsAsync(
        Guid environmentId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EndpointListItem>>([]);

    public static RegistryEndpointItem Endpoint { get; } = new(
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000001"),
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000002"),
        "Example client",
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000003"),
        "Example",
        Guid.Parse("6f1c9a20-0000-0000-0000-000000000004"),
        "Production",
        "https://example.com/",
        IsEnabled: true,
        IsDeleted: false,
        CanTest: true,
        Version: 1,
        EndpointMonitoringMode.Scheduled);

    public Task<IReadOnlyList<RegistryEndpointItem>> ListAllEndpointsAsync(
        RegistryAccessContext access,
        EndpointRegistryFilter? filter = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        filter ??= new EndpointRegistryFilter();
        var matches = (string.IsNullOrWhiteSpace(filter.Search)
                || Endpoint.DisplayUrl.Contains(filter.Search, StringComparison.OrdinalIgnoreCase)
                || Endpoint.WebsiteName.Contains(filter.Search, StringComparison.OrdinalIgnoreCase)
                || Endpoint.ClientName.Contains(filter.Search, StringComparison.OrdinalIgnoreCase))
            && (filter.ClientId is null || filter.ClientId == Endpoint.ClientId)
            && (filter.WebsiteId is null || filter.WebsiteId == Endpoint.WebsiteId)
            && (filter.EnvironmentId is null || filter.EnvironmentId == Endpoint.EnvironmentId);
        return Task.FromResult<IReadOnlyList<RegistryEndpointItem>>(matches ? [Endpoint] : []);
    }

    public Task<EndpointDetails?> FindEndpointAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<EndpointDetails?>(endpointId == BlockedEndpoint.Id ? BlockedEndpoint : null);

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
