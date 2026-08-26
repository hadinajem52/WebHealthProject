using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;

namespace WebHealth.Web.Controllers;

public sealed class AccountController : Controller
{
    private static readonly TimeSpan RememberMeDuration = TimeSpan.FromDays(14);

    private const string TwoFactorUnsupportedMessage =
        "This account has two-factor authentication enabled, and this application has no "
        + "second-factor challenge. Ask an Administrator to turn two-factor off for the account.";

    private const string InvalidSignInMessage =
        "That email address and password do not match an active account. Check both, and note "
        + "that a disabled account cannot sign in even with the right password.";

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(GetSafeReturnUrl(returnUrl));
        }

        return View(new LoginViewModel { ReturnUrl = GetSafeReturnUrl(returnUrl) });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        model.ReturnUrl = GetSafeReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null || user.IsDisabled)
        {
            ModelState.AddModelError(string.Empty, InvalidSignInMessage);
            return View(model);
        }

        var result = await signInManager.CheckPasswordSignInAsync(
            user,
            model.Password,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            if (await userManager.GetTwoFactorEnabledAsync(user))
            {
                ModelState.AddModelError(string.Empty, TwoFactorUnsupportedMessage);
                return View(model);
            }

            await signInManager.SignInAsync(user, BuildSignInProperties(model.RememberMe));
            return LocalRedirect(model.ReturnUrl);
        }

        ModelState.AddModelError(
            string.Empty,
            result.IsLockedOut
                ? "Too many failed attempts, so this account is locked for 15 minutes. "
                    + "Wait and try again, or ask an Administrator to reset the password."
                : InvalidSignInMessage);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(
        [FromServices] SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return RedirectToAction("HttpStatusCode", "Home", new { code = 403 });
    }

    private static AuthenticationProperties BuildSignInProperties(bool rememberMe)
    {
        return rememberMe
            ? new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(RememberMeDuration),
            }
            : new AuthenticationProperties { IsPersistent = false };
    }

    private string GetSafeReturnUrl(string? returnUrl)
    {
        return Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Index", "Home")!;
    }
}
