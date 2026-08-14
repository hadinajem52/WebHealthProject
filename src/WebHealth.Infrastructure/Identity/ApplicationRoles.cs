namespace WebHealth.Infrastructure.Identity;

public static class ApplicationRoles
{
    public const string Administrator = "Administrator";
    public const string Operations = "Operations";
    public const string DeveloperSupport = "Developer/Support";
    public const string Viewer = "Viewer";

    public static IReadOnlyList<RoleDefinition> All { get; } =
    [
        new(new Guid("7baf713e-5b13-4653-a65a-cb0e5af70860"), Administrator),
        new(new Guid("a7059ba0-b020-445c-b42c-566e493905a9"), Operations),
        new(new Guid("96cfe2cb-e156-451a-a78c-9307f51d5cc7"), DeveloperSupport),
        new(new Guid("d5991ea5-a72e-4e1e-bf56-120d0b0792a6"), Viewer)
    ];

    public sealed record RoleDefinition(Guid Id, string Name);
}
