using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Notifications;

namespace WebHealth.Web.Shell;

public sealed class NotificationsMenuViewComponent(
    INotificationFeedReader feedReader,
    ILogger<NotificationsMenuViewComponent> logger) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var principal = (ClaimsPrincipal)User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return View(NotificationFeed.Empty);
        }

        var userId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : Guid.Empty;

        try
        {
            var feed = await feedReader.GetForRecipientAsync(
                userId,
                principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity.Name,
                cancellationToken: HttpContext.RequestAborted);
            return View(feed);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The notification feed could not be read, so the header rendered without it. "
                + "CorrelationId={CorrelationId}",
                HttpContext.TraceIdentifier);
            return View(NotificationFeed.Unavailable);
        }
    }
}
