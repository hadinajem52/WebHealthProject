using Microsoft.AspNetCore.Mvc;

namespace WebHealth.Web.Shell;

public static class MissingRecordExtensions
{
    public const string ItemKey = "WebHealth.MissingRecord";

    public static IActionResult NotFoundRecord(this Controller controller, string noun)
    {
        controller.HttpContext.Items[ItemKey] = noun;
        return controller.NotFound();
    }

    public static string? MissingRecord(this HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var noun) ? noun as string : null;
}
