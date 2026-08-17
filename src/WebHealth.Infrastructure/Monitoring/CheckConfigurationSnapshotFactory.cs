using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

internal static class CheckConfigurationSnapshotFactory
{
    public static CheckConfigurationSnapshot Create(
        EndpointMonitor monitor,
        Guid logicalCheckId,
        DateTimeOffset now) => new()
        {
            LogicalCheckId = logicalCheckId,
            SchemaVersion = 1,
            MonitorType = monitor.MonitorType,
            ConfigurationFingerprint = monitor.ConfigurationFingerprint,
            IntervalSeconds = monitor.IntervalSeconds,
            TimeoutSeconds = monitor.TimeoutSeconds,
            FailureConfirmationCount = monitor.FailureConfirmationCount,
            RecoveryConfirmationCount = monitor.RecoveryConfirmationCount,
            WarningThresholdMs = monitor.WarningThresholdMs,
            CriticalThresholdMs = monitor.CriticalThresholdMs,
            IntervalSource = MonitorIntervalOverride.HasOverride(monitor.BoundedOverrides)
                ? ConfigurationValueSources.EndpointOverride
                : ConfigurationValueSources.EnvironmentDefault,
            TimeoutSource = ConfigurationValueSources.PolicyProfile,
            ConfirmationSource = ConfigurationValueSources.PolicyProfile,
            ThresholdSource = ConfigurationValueSources.PolicyProfile,
            CreatedAt = now
        };
}
