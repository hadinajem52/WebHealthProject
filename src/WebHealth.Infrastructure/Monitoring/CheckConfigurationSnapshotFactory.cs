using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

internal static class CheckConfigurationSnapshotFactory
{
    public static async Task LockAndRefreshAsync(
        ApplicationDbContext database,
        EndpointMonitor monitor,
        CancellationToken token)
    {
        await database.EndpointMonitors.FromSqlInterpolated($"""
            SELECT * FROM web_health.endpoint_monitor WHERE id = {monitor.Id} FOR UPDATE
            """).AsNoTracking().SingleAsync(token);
        await database.Entry(monitor).ReloadAsync(token);
        await database.Entry(monitor.Endpoint).ReloadAsync(token);
        await database.Entry(monitor.Endpoint.Environment).ReloadAsync(token);
    }

    public static CheckConfigurationSnapshot Create(
        EndpointMonitor monitor,
        Guid logicalCheckId,
        DateTimeOffset now) => new()
        {
            LogicalCheckId = logicalCheckId,
            SchemaVersion = 2,
            TargetNormalizedUrl = monitor.Endpoint.NormalizedUrl,
            TargetNormalizedHost = monitor.Endpoint.NormalizedHost,
            TargetEffectivePort = monitor.Endpoint.EffectivePort,
            TargetNormalizationVersion = monitor.Endpoint.NormalizationVersion,
            TargetIsProduction = monitor.Endpoint.Environment.IsProduction,
            CurrentTruthGeneration = monitor.CurrentTruthGeneration,
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
