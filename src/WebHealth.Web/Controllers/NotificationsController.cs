using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Notifications;

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

        // Only same-site destinations, so a crafted returnUrl cannot bounce the user off-site.
        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl!) : RedirectToAction("Index", "Home");
    }
}
