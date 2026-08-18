using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Infrastructure.Identity;

namespace WebHealth.Web.Controllers;

/// <summary>
/// The reporting entry points. Every action normalizes the request through the same
/// <see cref="ReportQueryNormalizer" /> and then calls the same <see cref="IReportingReader" />,
/// so the CSV a user downloads is produced from the filter they were looking at rather than from
/// a second interpretation of the same query string (AC-11).
/// </summary>
/// <remarks>
/// The dashboard views themselves arrive in increment 5.6. What exists here is the shared
/// entry: the export, and the trend series the charts will read.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class ReportsController(IReportingReader reportingReader) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Export(
        ReportQueryInput filter,
        CancellationToken cancellationToken = default)
    {
        var normalized = ReportQueryNormalizer.Normalize(filter, ReportMonitorTypes.All, DateTimeOffset.UtcNow);
        if (normalized.Query is not { } query)
        {
            return BadRequest(normalized.Errors);
        }

        try
        {
            // The reader re-slices the filter for export itself, so the file is the whole
            // filtered set rather than whichever page the screen happened to be on.
            var export = await reportingReader.ExportAsync(query, GetAccess(), cancellationToken);
            return File(
                ReportCsv.Write(export),
                "text/csv; charset=utf-8",
                ReportCsv.FileName(export.Query));
        }
        catch (ReportTooLargeException exception)
        {
            return BadRequest(new[] { exception.Message });
        }
    }

    /// <summary>The daily series the trend charts read (BR-U06).</summary>
    [HttpGet]
    public async Task<IActionResult> Trend(
        ReportQueryInput filter,
        CancellationToken cancellationToken = default)
    {
        var normalized = ReportQueryNormalizer.Normalize(filter, ReportMonitorTypes.All, DateTimeOffset.UtcNow);
        if (normalized.Query is not { } query)
        {
            return BadRequest(normalized.Errors);
        }

        try
        {
            var dataset = await reportingReader.QueryAsync(query, GetAccess(), cancellationToken);
            return Json(new
            {
                windowStart = dataset.Query.WindowStart,
                windowEnd = dataset.Query.WindowEnd,
                comparabilityWarning = dataset.Summary.Comparability.Warning,
                points = dataset.Trend
            });
        }
        catch (ReportTooLargeException exception)
        {
            return BadRequest(new[] { exception.Message });
        }
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}
