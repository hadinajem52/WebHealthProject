using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebHealth.Application.Administration;
using WebHealth.Application.Authorization;
using WebHealth.Application.Assignments;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.Administration)]
public sealed class AdministrationController(
    IUserAdministrationService userAdministration,
    ITeamAdministrationService teamAdministration) : Controller
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

    [HttpGet]
    public async Task<IActionResult> Teams(CancellationToken cancellationToken)
    {
        return View(new TeamListViewModel
        {
            Teams = await teamAdministration.ListTeamsAsync(cancellationToken)
        });
    }

    [HttpGet]
    public async Task<IActionResult> CreateTeam(CancellationToken cancellationToken)
    {
        return View(await BuildTeamFormAsync(new TeamFormViewModel(), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> CreateTeam(
        TeamFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildTeamFormAsync(model, cancellationToken));
        }

        var result = await teamAdministration.CreateTeamAsync(
            new CreateManagedTeam(model.Name, model.MemberUserIds),
            GetActorUserId(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(await BuildTeamFormAsync(model, cancellationToken));
        }

        TempData.AddFlashMessage(FlashLevel.Success, "Team created successfully.");
        return RedirectToAction(nameof(Teams));
    }

    [HttpGet]
    public async Task<IActionResult> EditTeam(Guid id, CancellationToken cancellationToken)
    {
        var team = await teamAdministration.FindTeamAsync(id, cancellationToken);
        if (team is null)
        {
            return NotFound();
        }

        return View(await BuildTeamFormAsync(new TeamFormViewModel
        {
            TeamId = team.Id,
            Name = team.Name,
            IsDisabled = team.IsDisabled,
            Version = team.Version,
            MemberUserIds = team.Members.Select(member => member.UserId).ToList()
        }, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> EditTeam(
        TeamFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildTeamFormAsync(model, cancellationToken));
        }

        var result = await teamAdministration.UpdateTeamAsync(
            new UpdateManagedTeam(
                model.TeamId,
                model.Name,
                model.IsDisabled,
                model.Version,
                model.MemberUserIds),
            GetActorUserId(),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(await BuildTeamFormAsync(model, cancellationToken));
        }

        TempData.AddFlashMessage(FlashLevel.Success, "Team assignment updated successfully.");
        return RedirectToAction(nameof(Teams));
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

    private async Task<TeamFormViewModel> BuildTeamFormAsync(
        TeamFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.AvailableUsers = (await userAdministration.ListUsersAsync(cancellationToken))
            .Where(user => !user.IsDisabled || model.MemberUserIds.Contains(user.Id))
            .ToArray();
        return model;
    }
}
