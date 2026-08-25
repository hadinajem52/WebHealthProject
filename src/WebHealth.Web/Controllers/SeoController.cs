using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Authorization;
using WebHealth.Application.Registry;
using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class SeoController(ISeoReader seoReader) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? applicability,
        string? environment,
        bool problemsOnly,
        string? subject,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var normalizedApplicability = SeoApplicabilities.Applicable.Equals(applicability, StringComparison.Ordinal)
            || SeoApplicabilities.NotApplicable.Equals(applicability, StringComparison.Ordinal)
                ? applicability
                : null;
        var normalizedEnvironment = environment is SeoQuery.Production or SeoQuery.NonProduction
            ? environment
            : null;
        var normalizedSubject = SeoFindingGroups.IsSelectable(subject) ? subject : null;

        var results = await seoReader.ListAsync(
            new(normalizedApplicability, normalizedEnvironment, problemsOnly, Subject: normalizedSubject),
            GetAccess(),
            page,
            cancellationToken);

        return View(new SeoListViewModel(
            results,
            normalizedApplicability,
            normalizedEnvironment,
            problemsOnly,
            normalizedSubject,
            SeoListViewModel.Describe(
                normalizedApplicability, normalizedEnvironment, problemsOnly, normalizedSubject)));
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}
