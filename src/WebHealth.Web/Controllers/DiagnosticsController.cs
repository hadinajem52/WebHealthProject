using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Infrastructure.Identity;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Diagnostics)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DiagnosticsController(IReportingReader reportingReader, TimeProvider timeProvider) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Monitoring(ReportQueryInput filter, CancellationToken cancellationToken)
    {
        var normalized = ReportQueryNormalizer.Normalize(filter, ReportMonitorTypes.All, timeProvider.GetUtcNow());
        if (normalized.Query is not { } query) return BadRequest(normalized.Errors);
        try
        {
            return View(await reportingReader.QueryDiagnosticsAsync(query, Access(), cancellationToken));
        }
        catch (ReportTooLargeException exception)
        {
            return BadRequest(new[] { exception.Message });
        }
    }

    [HttpGet("/health/monitoring")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var query = ReportQueryNormalizer.Normalize(new(), ReportMonitorTypes.All, timeProvider.GetUtcNow()).Query!;
        var diagnostics = await reportingReader.QueryDiagnosticsAsync(query, Access(), cancellationToken);
        var health = diagnostics.EngineHealth;
        return StatusCode(health?.Status is "Healthy" or "Warning" ? 200 : 503,
            new { status = health?.Status ?? "Unknown", reasons = health?.Reasons ?? [] });
    }

    private RegistryAccessContext Access() => new(
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty,
        ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
}
