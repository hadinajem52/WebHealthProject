using Microsoft.AspNetCore.Mvc;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Ajax;

public static class AjaxControllerExtensions
{
    public static IActionResult ValidationView(
        this Controller controller,
        string viewName,
        object model,
        int ajaxStatusCode = StatusCodes.Status422UnprocessableEntity)
    {
        if (controller.Request.IsWebHealthAjax())
        {
            controller.Response.StatusCode = ajaxStatusCode;
        }
        return controller.View(viewName, model);
    }

    public static IActionResult RedirectOrAjaxNavigate(
        this Controller controller,
        string redirectUrl,
        string message,
        FlashLevel level = FlashLevel.Success)
    {
        if (controller.Request.IsWebHealthAjax())
        {
            return controller.Ok(new AjaxFragmentViewModel(
                message,
                LevelName(level),
                RedirectUrl: LocalUrl(controller, redirectUrl)));
        }

        controller.TempData.AddFlashMessage(level, message);
        return controller.LocalRedirect(LocalUrl(controller, redirectUrl));
    }

    public static IActionResult RedirectOrAjaxRefresh(
        this Controller controller,
        string redirectUrl,
        string refreshUrl,
        string message,
        FlashLevel level,
        int ajaxStatusCode = StatusCodes.Status200OK)
    {
        if (controller.Request.IsWebHealthAjax())
        {
            return controller.StatusCode(ajaxStatusCode, new AjaxFragmentViewModel(
                message,
                LevelName(level),
                RefreshUrl: LocalUrl(controller, refreshUrl)));
        }

        controller.TempData.AddFlashMessage(level, message);
        return controller.LocalRedirect(LocalUrl(controller, redirectUrl));
    }

    public static IActionResult AjaxMessage(
        this Controller controller,
        string redirectUrl,
        string message,
        FlashLevel level,
        int ajaxStatusCode)
    {
        if (controller.Request.IsWebHealthAjax())
        {
            return controller.StatusCode(
                ajaxStatusCode,
                new AjaxFragmentViewModel(message, LevelName(level)));
        }

        controller.TempData.AddFlashMessage(level, message);
        return controller.LocalRedirect(LocalUrl(controller, redirectUrl));
    }

    private static string LocalUrl(Controller controller, string value) =>
        controller.Url.IsLocalUrl(value)
            ? value
            : throw new InvalidOperationException("AJAX navigation must remain local.");

    private static string LevelName(FlashLevel level) => level.ToString().ToLowerInvariant();
}
