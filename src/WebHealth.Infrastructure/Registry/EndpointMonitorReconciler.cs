using WebHealth.Infrastructure.Monitoring;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal static class EndpointMonitorReconciler
{
    public static void Archive(Endpoint endpoint, Guid actorId, DateTimeOffset now)
    {
        foreach (var monitor in endpoint.Monitors.Where(monitor => monitor.DeletedAt is null))
        {
            Retire(monitor, actorId, now);
        }
    }

    public static void Restore(
        ApplicationDbContext database,
        Endpoint endpoint,
        bool isProduction,
        Guid actorId,
        DateTimeOffset now)
    {
        var candidates = endpoint.Monitors
            .Where(monitor => monitor.DeletedAt == endpoint.DeletedAt
                && monitor.DeletedByUserId == endpoint.DeletedByUserId)
            .OrderByDescending(monitor => monitor.CreatedAt)
            .ThenBy(monitor => monitor.Id)
            .ToArray();
        var availability = RestoreOne(endpoint, candidates, RegistryDefaults.HttpAvailabilityMonitorType, actorId, now);
        if (availability is null)
        {
            availability = CreateMonitor(endpoint, isProduction, null, true, ResponseTimeThresholds.Default, actorId, now);
            database.EndpointMonitors.Add(availability);
        }

        if (RegistryDefaults.RequiresSslMonitor(endpoint.NormalizedUrl))
        {
            RestoreOne(endpoint, candidates, RegistryDefaults.SslCertificateMonitorType, actorId, now);
        }
        ReconcileSsl(database, endpoint, isProduction, availability.SchedulingEnabled, actorId, now);
    }

    public static void ReconcileSsl(
        ApplicationDbContext database,
        Endpoint endpoint,
        bool isProduction,
        bool schedulingEnabled,
        Guid actorId,
        DateTimeOffset now,
        bool tlsIdentityChanged = false)
    {
        var existing = endpoint.Monitors.SingleOrDefault(monitor => monitor.DeletedAt is null
            && monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType);
        var required = RegistryDefaults.RequiresSslMonitor(endpoint.NormalizedUrl);
        if (existing is not null && (!required || tlsIdentityChanged))
        {
            Retire(existing, actorId, now);
            existing = null;
        }
        if (required && existing is null)
        {
            database.EndpointMonitors.Add(CreateSslMonitor(endpoint, isProduction, schedulingEnabled, actorId, now));
        }
    }

    private static EndpointMonitor? RestoreOne(
        Endpoint endpoint,
        IEnumerable<EndpointMonitor> candidates,
        string monitorType,
        Guid actorId,
        DateTimeOffset now)
    {
        var active = endpoint.Monitors.SingleOrDefault(monitor => monitor.DeletedAt is null && monitor.MonitorType == monitorType);
        if (active is not null)
        {
            return active;
        }
        var selected = candidates.FirstOrDefault(monitor => monitor.MonitorType == monitorType);
        if (selected is not null)
        {
            selected.DeletedAt = null;
            selected.DeletedByUserId = null;
            Touch(selected, actorId, now);
        }
        return selected;
    }

    private static void Retire(EndpointMonitor monitor, Guid actorId, DateTimeOffset now)
    {
        monitor.DeletedAt = now;
        monitor.DeletedByUserId = actorId;
        Touch(monitor, actorId, now);
    }

    private static void Touch(EndpointMonitor monitor, Guid actorId, DateTimeOffset now)
    {
        monitor.UpdatedAt = now;
        monitor.UpdatedByUserId = actorId;
        monitor.Version++;
    }

    public static EndpointMonitor CreateMonitor(
        Endpoint endpoint,
        bool isProduction,
        int? intervalOverrideSeconds,
        bool schedulingEnabled,
        ResponseTimeThresholds thresholds,
        Guid actorId,
        DateTimeOffset now,
        HttpMonitorOverridesV2? overrides = null)
    {
        var interval = intervalOverrideSeconds ?? RegistryDefaults.GetHttpIntervalSeconds(isProduction);
        var schedule = MonitorCadence.Initialize(now);
        var monitor = new EndpointMonitor
        {
            Id = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            PolicyProfileId = RegistryDefaults.HttpAvailabilityPolicyProfileId,
            MonitorType = RegistryDefaults.HttpAvailabilityMonitorType,
            BoundedOverrides = MonitorIntervalOverride.Serialize(intervalOverrideSeconds),
            ConfigurationFingerprint = RegistryDefaults.CreateHttpFingerprint(
                endpoint.NormalizedUrl,
                isProduction,
                interval,
                RegistryDefaults.HttpTimeoutSeconds,
                2,
                2,
                thresholds.WarningMs,
                thresholds.CriticalMs),
            ScheduleAnchor = schedule.Anchor,
            NextDueAt = schedule.NextDueAt,
            IntervalSeconds = interval,
            TimeoutSeconds = RegistryDefaults.HttpTimeoutSeconds,
            FailureConfirmationCount = 2,
            RecoveryConfirmationCount = 2,
            WarningThresholdMs = thresholds.WarningMs,
            CriticalThresholdMs = thresholds.CriticalMs,
            SchedulingEnabled = schedulingEnabled,
            IsEnabled = true,
            CreatedAt = now,
            CreatedByUserId = actorId,
            UpdatedAt = now,
            UpdatedByUserId = actorId,
            Version = 1
        };
        HttpMonitorConfiguration.Apply(monitor, endpoint.NormalizedUrl, isProduction, overrides ?? new HttpMonitorOverridesV2
        {
            IntervalSeconds = intervalOverrideSeconds,
            WarningThresholdMs = thresholds.WarningMs == ResponseTimeThresholds.Default.WarningMs ? null : thresholds.WarningMs,
            CriticalThresholdMs = thresholds.CriticalMs == ResponseTimeThresholds.Default.CriticalMs ? null : thresholds.CriticalMs
        });
        return monitor;
    }

    public static EndpointMonitor CreateSslMonitor(
        Endpoint endpoint,
        bool isProduction,
        bool schedulingEnabled,
        Guid actorId,
        DateTimeOffset now)
    {
        var schedule = MonitorCadence.Initialize(now);
        return new EndpointMonitor
        {
            Id = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            PolicyProfileId = RegistryDefaults.SslCertificatePolicyProfileId,
            MonitorType = RegistryDefaults.SslCertificateMonitorType,
            BoundedOverrides = MonitorIntervalOverride.Serialize(null),
            ConfigurationFingerprint = RegistryDefaults.CreateSslFingerprint(
                endpoint.NormalizedUrl, isProduction),
            ScheduleAnchor = schedule.Anchor,
            NextDueAt = schedule.NextDueAt,
            IntervalSeconds = RegistryDefaults.SslIntervalSeconds,
            TimeoutSeconds = RegistryDefaults.SslTimeoutSeconds,
            FailureConfirmationCount = RegistryDefaults.SslFailureConfirmationCount,
            RecoveryConfirmationCount = RegistryDefaults.SslRecoveryConfirmationCount,
            WarningThresholdMs = null,
            CriticalThresholdMs = null,
            SchedulingEnabled = schedulingEnabled,
            IsEnabled = true,
            CreatedAt = now,
            CreatedByUserId = actorId,
            UpdatedAt = now,
            UpdatedByUserId = actorId,
            Version = 1
        };
    }

}
