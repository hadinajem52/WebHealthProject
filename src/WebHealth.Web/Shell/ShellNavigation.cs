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
        // The three audit surfaces answer the same question — what a crawl of the site found —
        // so they group under one heading rather than competing with Dashboard and Incidents for
        // top-level attention. Each carries its own glyph: they previously shared the bar-chart
        // icon, which left the sidebar with three entries that looked identical.
        new("Audits",
        [
            new NavigationItem(
                "SEO",
                "seo",
                "Seo",
                "Index",
                ["Administrator", "Operations", "Developer/Support", "Viewer"]),
            new NavigationItem(
                "Broken links",
                "broken-link",
                "Crawl",
                "Index",
                ["Administrator", "Operations", "Developer/Support", "Viewer"]),
            new NavigationItem(
                "PageSpeed",
                "speed",
                "PageAudits",
                "Index",
                ["Administrator", "Operations", "Developer/Support", "Viewer"])
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
