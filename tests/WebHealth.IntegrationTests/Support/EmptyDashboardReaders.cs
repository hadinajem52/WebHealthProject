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
        HasTargetAuthorization: true,
        TargetAuthorizationKind: TargetAuthorizationKinds.Owned,
        TargetAuthorizationEvidence: "Owned test target",
        TargetAuthorizationExpiresAt: null,
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
        Task.FromResult<IReadOnlyList<EnvironmentListItem>>([]);

    public Task<IReadOnlyList<EnvironmentListItem>> ListAllEnvironmentsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EnvironmentListItem>>([]);

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
        CanTest: true,
        Version: 1,
        EndpointMonitoringMode.Scheduled);

    public Task<IReadOnlyList<RegistryEndpointItem>> ListAllEndpointsAsync(
        RegistryAccessContext access,
        string? search = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RegistryEndpointItem>>([Endpoint]);

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
