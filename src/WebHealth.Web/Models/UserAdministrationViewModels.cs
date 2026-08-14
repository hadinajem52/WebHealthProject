using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Administration;

namespace WebHealth.Web.Models;

public sealed class UserListViewModel
{
    public required IReadOnlyList<ManagedUser> Users { get; init; }
}

public sealed class CreateUserViewModel
{
    [Required, StringLength(200)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 12)]
    [Display(Name = "Initial password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Roles")]
    public List<string> Roles { get; set; } = [];

}

public sealed class EditUserViewModel
{
    public Guid UserId { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [Display(Name = "Account disabled")]
    public bool IsDisabled { get; set; }

    [Display(Name = "Roles")]
    public List<string> Roles { get; set; } = [];

    [DataType(DataType.Password), StringLength(128, MinimumLength = 12)]
    [Display(Name = "New password")]
    public string? NewPassword { get; set; }
}
