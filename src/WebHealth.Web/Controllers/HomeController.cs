using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;
using WebHealth.Web.Ajax;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public class HomeController(
    IReportingReader reportingReader,
    IRegistryReader registryReader,
    ITargetRegistryReader targetReader,
    TimeProvider timeProvider) : Controller
{
    private const int ActiveIncidentPreviewCount = 8;

    public async Task<IActionResult> Index(
        DashboardFilterViewModel filter,
        CancellationToken cancellationToken = default)
    {
        var access = GetAccess();
        var asOf = timeProvider.GetUtcNow();
        var options = await LoadOptionsAsync(access, cancellationToken);
        var normalized = ReportQueryNormalizer.Normalize(filter.ToInput(), ReportMonitorTypes.All, asOf);

        if (normalized.Query is not { } query)
        {
            return View(EmptyDashboard(filter, options, asOf, normalized.Errors));
        }

        try
        {
            var dataset = await reportingReader.QueryAsync(query, access, cancellationToken);
            var certificates = await reportingReader.QueryCertificateExpiryAsync(query, access, cancellationToken);
            var diagnostics = await reportingReader.QueryDiagnosticsAsync(query, access, cancellationToken);
            var incidents = await reportingReader.QueryActiveIncidentsAsync(
                query, access, ActiveIncidentPreviewCount, cancellationToken);

            return View(new DashboardViewModel(
                filter,
                options,
                Describe(dataset.Query, asOf, options, dataset.Summary.Comparability.Warning),
                dataset,
                certificates,
                diagnostics,
                incidents,
                []));
        }
        catch (ReportTooLargeException exception)
        {
            return View(EmptyDashboard(filter, options, asOf, [exception.Message]));
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [AllowAnonymous]
    public IActionResult Error()
    {
        if (Request.IsWebHealthAjax())
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "The request could not be completed.",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = HttpContext.TraceIdentifier
                });
        }

        return View(ErrorViewModel.Create(500, HttpContext.TraceIdentifier, GetRetryUrl()));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [AllowAnonymous]
    public IActionResult HttpStatusCode(int code)
    {
        if (code is < 400 or > 599)
        {
            return BadRequest();
        }

        Response.StatusCode = code;
        return View("Error", ErrorViewModel.Create(
            code, HttpContext.TraceIdentifier, GetRetryUrl(), HttpContext.MissingRecord()));
    }

    private async Task<DashboardFilterOptions> LoadOptionsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken) => new(
        await registryReader.ListClientsAsync(access, cancellationToken),
        await registryReader.ListWebsitesAsync(access, cancellationToken: cancellationToken),
        await targetReader.ListAllEnvironmentsAsync(access, cancellationToken),
        await registryReader.ListOwnersAsync(cancellationToken: cancellationToken));

    private static FilterSummaryViewModel Describe(
        ReportQuery query,
        DateTimeOffset asOf,
        DashboardFilterOptions options,
        string? comparabilityWarning)
    {
        var filters = new List<FilterSummaryItem>();
        if (query.ClientId is { } clientId)
        {
            filters.Add(new("Client", Name(options.Clients.FirstOrDefault(item => item.Id == clientId)?.Name)));
        }

        if (query.WebsiteId is { } websiteId)
        {
            filters.Add(new("Website", Name(options.Websites.FirstOrDefault(item => item.Id == websiteId)?.Name)));
        }

        if (query.EnvironmentId is { } environmentId)
        {
            var environment = options.Environments.FirstOrDefault(item => item.Id == environmentId);
            filters.Add(new(
                "Environment",
                environment is null ? Name(null) : $"{environment.WebsiteName} — {environment.Name}"));
        }

        if (query.OwnerSubjectId is { } ownerSubjectId)
        {
            filters.Add(new(
                "Owner",
                Name(options.Owners
                    .FirstOrDefault(item => item.OwnerSubjectId == ownerSubjectId)?.DisplayName)));
        }

        if (query.HealthStatus is { } status)
        {
            filters.Add(new("Health status", status));
        }

        if (query.MonitorType is { } monitorType)
        {
            filters.Add(new("Monitor type", monitorType));
        }

        return new(
            asOf,
            filters,
            new FilterSummaryWindow(query.WindowStart, query.WindowEnd),
            comparabilityWarning);
    }

    private static string Name(string? value) => value ?? "No longer visible";

    private static DashboardViewModel EmptyDashboard(
        DashboardFilterViewModel filter,
        DashboardFilterOptions options,
        DateTimeOffset asOf,
        IReadOnlyList<string> errors) => new(
        filter,
        options,
        new FilterSummaryViewModel(asOf, []),
        new ReportDataset(
            ReportQueryNormalizer.Normalize(new ReportQueryInput(), ReportMonitorTypes.All, asOf).Query!,
            new ReportSummary(
                0, 0, 0, 0, 0, 0, 0, 0,
                new ReportUptime(0, 0, 0, 0, 0),
                new ReportResponseTimes(null, null, 0),
                PerformanceComparability.Evaluate([])),
            [],
            [],
            0),
        ReportCertificateExpiry.Empty,
        ReportDiagnostics.Empty,
        [],
        errors);

    private string? GetRetryUrl()
    {
        var originalPath = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath
            ?? HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path;

        return Url.IsLocalUrl(originalPath) ? originalPath : null;
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}
