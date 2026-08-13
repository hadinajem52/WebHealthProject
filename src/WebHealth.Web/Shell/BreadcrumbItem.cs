namespace WebHealth.Web.Shell;

/// <summary>
/// One breadcrumb entry. The last entry is rendered as the current page and is
/// never a link.
/// </summary>
/// <param name="Text">Visible label.</param>
/// <param name="Url">Destination, or <see langword="null" /> when the entry is not navigable.</param>
public sealed record BreadcrumbItem(string Text, string? Url = null);
