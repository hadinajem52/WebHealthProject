namespace WebHealth.Application.Authorization;

public static class AuthorizationPolicies
{
    public const string Administration = "Administration";
    public const string Diagnostics = "Diagnostics";
    public const string OperateMonitoring = "OperateMonitoring";
    public const string ReadAllOperationalData = "ReadAllOperationalData";
    public const string ViewAuditHistory = "ViewAuditHistory";
    public const string ReadRegistry = "ReadRegistry";
    public const string ManageRegistry = "ManageRegistry";
    public const string TestRegistryTargets = "TestRegistryTargets";
}
