using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Administration;
using WebHealth.Application.Authorization;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Administration)]
public sealed class AdministrationController(IUserAdministrationService userAdministration) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Users(CancellationToken cancellationToken)
    {
        return View(new UserListViewModel
        {
            Users = await userAdministration.ListUsersAsync(cancellationToken)
        });
    }

    [HttpGet]
    public IActionResult CreateUser()
    {
        return View(new CreateUserViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(
        CreateUserViewModel model,
        CancellationToken cancellationToken)
    {
        ValidateRoleSelection(model.Roles);
        if (!ModelState.IsValid)
        {
            model.Password = string.Empty;
            return View(model);
        }

        var result = await userAdministration.CreateUserAsync(
            new CreateManagedUser(model.DisplayName, model.Email, model.Password, model.Roles),
            GetActorUserId(),
            cancellationToken);
        model.Password = string.Empty;
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        TempData.AddFlashMessage(FlashLevel.Success, "User created successfully.");
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> EditUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await userAdministration.FindUserAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return View(new EditUserViewModel
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            IsDisabled = user.IsDisabled,
            Roles = user.Roles.ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditUser(
        EditUserViewModel model,
        CancellationToken cancellationToken)
    {
        ValidateRoleSelection(model.Roles);
        if (!ModelState.IsValid)
        {
            model.NewPassword = null;
            await RestoreEmailAsync(model, cancellationToken);
            return View(model);
        }

        var result = await userAdministration.UpdateUserAsync(
            new UpdateManagedUser(
                model.UserId,
                model.DisplayName,
                model.IsDisabled,
                model.Roles,
                model.NewPassword),
            GetActorUserId(),
            cancellationToken);
        if (!result.Succeeded)
        {
            model.NewPassword = null;
            AddErrors(result.Errors);
            await RestoreEmailAsync(model, cancellationToken);
            return View(model);
        }

        model.NewPassword = null;
        TempData.AddFlashMessage(FlashLevel.Success, "User access updated successfully.");
        return RedirectToAction(nameof(Users));
    }

    private Guid GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
    }

    private void ValidateRoleSelection(IReadOnlyCollection<string> roles)
    {
        if (roles.Count == 0)
        {
            ModelState.AddModelError(nameof(CreateUserViewModel.Roles), "Select at least one role.");
        }

        var supportedRoles = ApplicationRoles.All.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        if (roles.Any(role => !supportedRoles.Contains(role)))
        {
            ModelState.AddModelError(nameof(CreateUserViewModel.Roles), "One or more selected roles are invalid.");
        }
    }

    private void AddErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }

    private async Task RestoreEmailAsync(EditUserViewModel model, CancellationToken cancellationToken)
    {
        var user = await userAdministration.FindUserAsync(model.UserId, cancellationToken);
        model.Email = user?.Email ?? string.Empty;
    }
}
