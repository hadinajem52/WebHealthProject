using Microsoft.AspNetCore.Mvc;
using WebHealth.Web.Models;

namespace WebHealth.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
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
        return View("Error", ErrorViewModel.Create(code, HttpContext.TraceIdentifier));
    }
}
