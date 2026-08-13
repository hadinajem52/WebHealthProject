using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Web.Models;

namespace WebHealth.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(ErrorViewModel.Create(500, HttpContext.TraceIdentifier));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult HttpStatusCode(int code)
    {
        if (code is < 400 or > 599)
        {
            return BadRequest();
        }

        Response.StatusCode = code;
        return View("Error", ErrorViewModel.Create(code, HttpContext.TraceIdentifier, GetRetryUrl()));
    }

    // Only the re-executed original path is offered as a retry target, and only
    // when it is a local URL.
    private string? GetRetryUrl()
    {
        var originalPath = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath;

        return Url.IsLocalUrl(originalPath) ? originalPath : null;
    }
}
