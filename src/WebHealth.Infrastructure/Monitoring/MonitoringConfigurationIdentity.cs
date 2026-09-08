namespace WebHealth.Infrastructure.Monitoring;

internal static class MonitoringConfigurationIdentity
{
    public static string Format(string fingerprint, short schemaVersion, long? generation) =>
        FormattableString.Invariant($"{fingerprint}:{schemaVersion}:{generation}\n");
}
