using WebHealth.Domain.Monitoring;

namespace WebHealth.Application.Monitoring;

/// <summary>
/// Maps a monitor type onto the durable work that carries out its check. Every producer of
/// durable work goes through here so a monitor type can never be queued as the wrong kind of
/// work — which would hand an SSL check to the HTTP executor.
///
/// The mapping is exhaustive on purpose: an unrecognised monitor type is a bug or an
/// unfinished feature, and defaulting it to HTTP would run it against the wrong executor
/// instead of failing where the mistake is.
/// </summary>
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
