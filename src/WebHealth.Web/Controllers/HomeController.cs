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

namespace WebHealth.Web.Controllers;

/// <summary>
/// The dashboard is a read surface over the registry, so it carries the same policy every other
/// read surface does. Relying on the authenticated-user fallback would let a signed-in account
/// with no application role reach it and see an empty page instead of a denial, which hides the
/// authorization decision inside query behaviour rather than stating it at the boundary.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public class HomeController(
    IReportingReader reportingReader,
    IRegistryReader registryReader,
    ITargetRegistryReader targetReader,
    TimeProvider timeProvider) : Controller
{
    private const int ActiveIncidentPreviewCount = 8;

    /// <summary>
    /// Every card, the table, the chart and the incident list are read from one
    /// <see cref="ReportQuery" /> through the shared reporting layer, so changing a filter
    /// recomputes all of them consistently and none of them can quietly answer a different
    /// question (BR-R01).
    /// </summary>
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
            // The filter is shown back with its errors rather than silently reset, so the
            // reader can see which value was rejected.
            return View(EmptyDashboard(filter, options, asOf, normalized.Errors));
        }

        try
        {
            var dataset = await reportingReader.QueryAsync(query, access, cancellationToken);
            var certificates = await reportingReader.QueryCertificateExpiryAsync(query, access, cancellationToken);
            var diagnostics = await reportingReader.QueryDiagnosticsAsync(query, access, cancellationToken);
            // The incident list comes from the same filtered selection as the incident count on
            // the card above it, so the two can never describe different sets.
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
        return View(ErrorViewModel.Create(500, HttpContext.TraceIdentifier));
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
        return View("Error", ErrorViewModel.Create(code, HttpContext.TraceIdentifier, GetRetryUrl()));
    }

    private async Task<DashboardFilterOptions> LoadOptionsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken) => new(
        await registryReader.ListClientsAsync(access, cancellationToken),
        await registryReader.ListWebsitesAsync(access, cancellationToken: cancellationToken),
        await targetReader.ListAllEnvironmentsAsync(access, cancellationToken),
        await registryReader.ListOwnersAsync(cancellationToken: cancellationToken));

    /// <summary>
    /// BR-R01. The disclosure is built from the <em>query the reader served</em> rather than from
    /// the submitted form, so what it names is what was actually applied — including a window the
    /// server defaulted or bounded and a page it clamped.
    /// </summary>
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
            // Stated as half-open, because that is what it is: a sample at the end instant
            // belongs to the next period (BR-U04). The instants are passed through rather than
            // formatted here: a sentence built in the controller cannot be re-rendered in the
            // reader's time zone.
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
                0, 0, 0, 0, 0, 0, 0,
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

    // Only the re-executed original path is offered as a retry target, and only
    // when it is a local URL.
    private string? GetRetryUrl()
    {
        var originalPath = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath;

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
