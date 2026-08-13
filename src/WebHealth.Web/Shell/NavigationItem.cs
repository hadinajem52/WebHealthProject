namespace WebHealth.Web.Shell;

/// <summary>
/// A single primary-navigation entry. Navigation is a convenience only; every
/// protected operation is authorized server-side regardless of what is rendered.
/// </summary>
/// <param name="Text">Visible label.</param>
/// <param name="IconKey">Key resolved by the shared icon partial.</param>
/// <param name="Controller">Target controller, or <see langword="null" /> when the destination is not implemented yet.</param>
/// <param name="Action">Target action, or <see langword="null" /> when the destination is not implemented yet.</param>
public sealed record NavigationItem(
    string Text,
    string IconKey,
    string? Controller = null,
    string? Action = null)
{
    /// <summary>Gets a value indicating whether the destination exists in the current build.</summary>
    public bool IsAvailable => Controller is not null && Action is not null;

    /// <summary>Determines whether this entry is the active route.</summary>
    public bool IsCurrent(string? controller, string? action)
    {
        return IsAvailable
            && string.Equals(Controller, controller, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Action, action, StringComparison.OrdinalIgnoreCase);
    }
}
