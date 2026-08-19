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

/// <summary>
/// AC-07's view. Read-only for every persona that may read the registry, because an SEO result is
/// an observation about a site rather than an operational control — the policy that decides what a
/// site *should* declare stays behind <see cref="AuthorizationPolicies.ManageRegistry" /> where
/// 6.4 put it.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.ReadRegistry)]
public sealed class SeoController(ISeoReader seoReader) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? applicability,
        string? environment,
        bool problemsOnly,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        // Unrecognised filter values become no filter rather than an error: a stale bookmark should
        // show the unfiltered list, not a failure. Anything that survives is passed to the reader,
        // which applies it in the database.
        var normalizedApplicability = SeoApplicabilities.Applicable.Equals(applicability, StringComparison.Ordinal)
            || SeoApplicabilities.NotApplicable.Equals(applicability, StringComparison.Ordinal)
                ? applicability
                : null;
        var normalizedEnvironment = environment is SeoQuery.Production or SeoQuery.NonProduction
            ? environment
            : null;

        var results = await seoReader.ListAsync(
            new(normalizedApplicability, normalizedEnvironment, problemsOnly),
            GetAccess(),
            page,
            cancellationToken);

        return View(new SeoListViewModel(
            results,
            normalizedApplicability,
            normalizedEnvironment,
            problemsOnly,
            SeoListViewModel.Describe(normalizedApplicability, normalizedEnvironment, problemsOnly)));
    }

    private RegistryAccessContext GetAccess()
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(userId, ApplicationRoles.All.Select(role => role.Name).Where(User.IsInRole).ToArray());
    }
}
