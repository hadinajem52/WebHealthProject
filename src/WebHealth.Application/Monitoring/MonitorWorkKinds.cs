using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Monitoring;

public static class MonitorWorkKinds
{
    public static string For(string monitorType) => monitorType switch
    {
        HttpIssueIdentity.MonitorType => DurableWorkKinds.HttpCheck,
        SslMonitorIdentity.MonitorType => DurableWorkKinds.SslCheck,
        _ => throw new InvalidOperationException($"Monitor type '{monitorType}' has no durable work kind.")
    };

    public static string CreateDedupeKey(Guid logicalCheckId, string monitorType) =>
        $"v1|{logicalCheckId:N}|{DedupeSuffix(monitorType)}";

    public static bool IsSsl(string monitorType) => monitorType == SslMonitorIdentity.MonitorType;

    private static string DedupeSuffix(string monitorType) => For(monitorType) switch
    {
        DurableWorkKinds.SslCheck => "ssl-check",
        _ => "http-check"
    };
}
