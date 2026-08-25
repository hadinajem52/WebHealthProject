namespace WebHealth.Web.Shell;

public sealed record NavigationItem(
    string Text,
    string IconKey,
    string? Controller = null,
    string? Action = null,
    IReadOnlyCollection<string>? RequiredRoles = null,
    IReadOnlyCollection<string>? CurrentControllers = null)
{
    public bool IsAvailable => Controller is not null && Action is not null;

    public bool IsVisible(System.Security.Claims.ClaimsPrincipal user) =>
        RequiredRoles is null || RequiredRoles.Any(user.IsInRole);

    public bool IsCurrent(string? controller, string? action)
    {
        return IsAvailable && (
            CurrentControllers?.Contains(controller ?? string.Empty, StringComparer.OrdinalIgnoreCase) == true
            || string.Equals(Controller, controller, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Action, action, StringComparison.OrdinalIgnoreCase));
    }
}
