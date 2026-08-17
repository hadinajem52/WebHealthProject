namespace WebHealth.Web.Shell;

/// <summary>
/// The primary navigation defined in <c>docs/phase-0/UI_Direction.md</c> section 4.
/// Entries without a destination are rendered as planned and are not links.
/// Navigation visibility mirrors server-side role policies for usability but
/// never replaces authorization on the destination.
/// </summary>
public static class ShellNavigation
{
    private static readonly NavigationSection[] SectionList =
    [
        new(null,
        [
            new NavigationItem("Dashboard", "dashboard", "Home", "Index"),
            new NavigationItem(
                "Registry",
                "registry",
                "Registry",
                "Clients",
                ["Administrator", "Operations", "Developer/Support", "Viewer"]),
            new NavigationItem("Maintenance", "warning", "Maintenance", "Index", ["Administrator", "Operations"]),
            new NavigationItem(
                "Incidents",
                "incidents",
                "Incidents",
                "Index",
                ["Administrator", "Operations", "Developer/Support", "Viewer"]),
            new NavigationItem("Reports", "reports")
        ]),
        new("Administration",
        [
            new NavigationItem("Users", "users", "Administration", "Users", ["Administrator"]),
            new NavigationItem("Teams", "users", "Administration", "Teams", ["Administrator"]),
            new NavigationItem("Audit", "audit", "Audit", "Index", ["Administrator", "Operations"]),
            new NavigationItem("Diagnostics", "diagnostics")
        ])
    ];

    /// <summary>Gets the navigation groups in render order.</summary>
    public static IReadOnlyList<NavigationSection> Sections => SectionList;
}
