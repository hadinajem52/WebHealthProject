using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Infrastructure.Identity;

namespace WebHealth.Web.Controllers;

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
