using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Administration;

namespace WebHealth.Web.Models;

public sealed class UserListViewModel
{
    public required IReadOnlyList<ManagedUser> Users { get; init; }
}

public sealed class CreateUserViewModel
{
    [Required(ErrorMessage = "Enter a display name. It is how this person appears throughout the app.")]
    [StringLength(200, ErrorMessage = "This name is too long. Use 200 characters or fewer.")]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter an email address. It is also the sign-in name.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address, such as name@example.com.")]
    [StringLength(256, ErrorMessage = "This email address is too long. Use 256 characters or fewer.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter an initial password. Use at least 12 characters, with an uppercase letter, a lowercase letter, a digit, a symbol, and at least 4 different characters.")]
    [DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 12,
        ErrorMessage = "The password must be between 12 and 128 characters. Use at least 12 characters, with an uppercase letter, a lowercase letter, a digit, a symbol, and at least 4 different characters.")]
    [Display(Name = "Initial password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Roles")]
    public List<string> Roles { get; set; } = [];

}

public sealed class EditUserViewModel
{
    public Guid UserId { get; set; }

    [Required(ErrorMessage = "Enter a display name. It is how this person appears throughout the app.")]
    [StringLength(200, ErrorMessage = "This name is too long. Use 200 characters or fewer.")]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [Display(Name = "Account disabled")]
    public bool IsDisabled { get; set; }

    [Display(Name = "Roles")]
    public List<string> Roles { get; set; } = [];

    [DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 12,
        ErrorMessage = "The new password must be between 12 and 128 characters. Use at least 12 characters, with an uppercase letter, a lowercase letter, a digit, a symbol, and at least 4 different characters.")]
    [Display(Name = "New password")]
    public string? NewPassword { get; set; }

    [Display(Name = "Notification delivery")]
    public string NotificationRouting { get; set; } = NotificationRoutingModes.SignInEmail;

    [EmailAddress(ErrorMessage = "Enter a valid email address, such as alerts@example.com.")]
    [StringLength(320, ErrorMessage = "This address is too long. Use 320 characters or fewer.")]
    [Display(Name = "Send notifications to")]
    public string? NotificationEmail { get; set; }

    public bool RoutesToDifferentAddress =>
        string.Equals(NotificationRouting, NotificationRoutingModes.CustomEmail, StringComparison.Ordinal);
}

public static class NotificationRoutingModes
{
    public const string SignInEmail = "SignInEmail";
    public const string CustomEmail = "CustomEmail";
}
