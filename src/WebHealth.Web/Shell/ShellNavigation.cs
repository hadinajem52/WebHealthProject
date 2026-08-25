namespace WebHealth.Web.Shell;

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
                "Targets",
                "Endpoints",
                ["Administrator", "Operations", "Developer/Support", "Viewer"],
                ["Registry", "Targets"]),
            new NavigationItem("Maintenance", "warning", "Maintenance", "Index", ["Administrator", "Operations"]),
            new NavigationItem(
                "Incidents",
                "incidents",
                "Incidents",
                "Index",
                ["Administrator", "Operations", "Developer/Support", "Viewer"])
        ]),
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
            new NavigationItem("Audit", "audit", "Audit", "Index", ["Administrator", "Operations"])
        ])
    ];

    public static IReadOnlyList<NavigationSection> Sections => SectionList;
}
