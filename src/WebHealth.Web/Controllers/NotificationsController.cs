using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Notifications;
using WebHealth.Web.Ajax;

namespace WebHealth.Web.Controllers;

[Authorize]
public sealed class NotificationsController(INotificationFeedReader feedReader) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(string? returnUrl, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await feedReader.MarkReadAsync(userId, cancellationToken);
        }

        if (Request.IsWebHealthAjax())
        {
            return Ok(new AjaxFragmentViewModel(
                RefreshUrl: Url.Action(nameof(Menu))));
        }

        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl!) : RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Menu() => ViewComponent("NotificationsMenu");
}
