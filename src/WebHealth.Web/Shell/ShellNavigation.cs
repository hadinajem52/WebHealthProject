namespace WebHealth.Web.Shell;

/// <summary>
/// The primary navigation defined in <c>docs/phase-0/UI_Direction.md</c> section 4.
/// Entries without a destination are rendered as planned and are not links.
/// Role-dependent visibility is Phase 2 work; navigation never replaces
/// server-side authorization.
/// </summary>
public static class ShellNavigation
{
    private static readonly NavigationSection[] SectionList =
    [
        new(null,
        [
            new NavigationItem("Dashboard", "dashboard", "Home", "Index"),
            new NavigationItem("Registry", "registry"),
            new NavigationItem("Incidents", "incidents"),
            new NavigationItem("Reports", "reports")
        ]),
        new("Administration",
        [
            new NavigationItem("Users", "users"),
            new NavigationItem("Audit", "audit"),
            new NavigationItem("Diagnostics", "diagnostics")
        ])
    ];

    /// <summary>Gets the navigation groups in render order.</summary>
    public static IReadOnlyList<NavigationSection> Sections => SectionList;
}
