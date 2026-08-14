namespace WebHealth.Application.Authorization;

public static class AuthorizationPolicies
{
    public const string Administration = "Administration";
    public const string Diagnostics = "Diagnostics";
    public const string OperateMonitoring = "OperateMonitoring";
    public const string ReadAllOperationalData = "ReadAllOperationalData";
    public const string ViewAuditHistory = "ViewAuditHistory";
}
