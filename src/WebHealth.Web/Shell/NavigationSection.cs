namespace WebHealth.Web.Shell;

public sealed record NavigationSection(string? Heading, IReadOnlyList<NavigationItem> Items);
