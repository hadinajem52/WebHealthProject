namespace WebHealth.Web.Shell;

/// <summary>A labelled group of primary-navigation entries.</summary>
/// <param name="Heading">Group heading, or <see langword="null" /> for the leading group.</param>
/// <param name="Items">Entries rendered in the group.</param>
public sealed record NavigationSection(string? Heading, IReadOnlyList<NavigationItem> Items);
