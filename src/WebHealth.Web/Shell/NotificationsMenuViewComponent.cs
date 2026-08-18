using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Notifications;

namespace WebHealth.Web.Shell;

/// <summary>
/// Renders the header notification panel. A view component keeps the layout self-contained so
/// every controller does not have to supply feed data on every page.
/// </summary>
public sealed class NotificationsMenuViewComponent(INotificationFeedReader feedReader) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var principal = (ClaimsPrincipal)User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return View(new NotificationFeed([], 0));
        }

        var userId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : Guid.Empty;
        var feed = await feedReader.GetForRecipientAsync(
            userId,
            principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity.Name,
            cancellationToken: HttpContext.RequestAborted);
        return View(feed);
    }
}
