using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Auditing;
using WebHealth.Application.Authorization;
using WebHealth.Web.Models;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.ViewAuditHistory)]
public sealed class AuditController(IAuditTrailReader auditTrail) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        DateOnly? fromDate,
        DateOnly? toDate,
        Guid? actorUserId,
        string? action,
        string? entity,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (fromDate > toDate)
        {
            ModelState.AddModelError(string.Empty, "The from date must be on or before the to date.");
        }

        var query = new AuditSearchQuery(fromDate, toDate, actorUserId, action, entity, page);
        return View(new AuditIndexViewModel
        {
            FromDate = fromDate,
            ToDate = toDate,
            ActorUserId = actorUserId,
            Action = action,
            Entity = entity,
            Result = ModelState.IsValid
                ? await auditTrail.SearchAsync(query, cancellationToken)
                : new AuditSearchResult([], 1, query.PageSize, 0),
            Actors = await auditTrail.ListActorsAsync(cancellationToken)
        });
    }
}
