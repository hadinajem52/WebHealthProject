using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Archiving;
using WebHealth.Web.Ajax;

namespace WebHealth.Web.Shell;

public static class RunHistoryArchiveResults
{
    public static IActionResult ArchiveOutcome(
        this Controller controller,
        RunHistoryArchiveResult result,
        string redirectUrl,
        string noun) => result.Status switch
        {
            RunHistoryArchiveStatus.Forbidden => controller.Forbid(),
            RunHistoryArchiveStatus.NotFound => controller.NotFoundRecord("endpoint"),
            RunHistoryArchiveStatus.Succeeded => controller.RedirectOrAjaxRefresh(
                redirectUrl,
                redirectUrl,
                result.AffectedCount == 0
                    ? $"There was no finished {noun} to clear."
                    : $"{result.AffectedCount} {noun}{(result.AffectedCount == 1 ? null : "s")} "
                        + "moved to the archive.",
                result.AffectedCount == 0 ? FlashLevel.Information : FlashLevel.Success),
            _ => controller.AjaxMessage(
                redirectUrl,
                result.Error ?? $"The {noun} history could not be cleared.",
                FlashLevel.Error,
                StatusCodes.Status422UnprocessableEntity,
                redirectUrl)
        };

    public static IActionResult RestoreOutcome(
        this Controller controller,
        RunHistoryArchiveResult result,
        string redirectUrl,
        string noun) => result.Status switch
        {
            RunHistoryArchiveStatus.Forbidden => controller.Forbid(),
            RunHistoryArchiveStatus.NotFound => controller.NotFoundRecord(noun),
            RunHistoryArchiveStatus.Succeeded => controller.RedirectOrAjaxRefresh(
                redirectUrl,
                redirectUrl,
                $"The {noun} was restored from the archive.",
                FlashLevel.Success),
            _ => controller.AjaxMessage(
                redirectUrl,
                result.Error ?? $"The {noun} could not be restored.",
                FlashLevel.Error,
                StatusCodes.Status422UnprocessableEntity,
                redirectUrl)
        };
}
