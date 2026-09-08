using Microsoft.Extensions.Logging;
using System.Text.Json;
using WebHealth.Application.Monitoring;
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
        DateTimeOffset now,
        ILogger logger)
    {
        ResolvedHttpCheckConfiguration? resolvedHttp;
        try
        {
            resolvedHttp = monitor.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType
                ? HttpMonitorConfiguration.ResolveCheck(monitor)
                : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            logger.LogError("HTTP configuration rejected before check creation for monitor {MonitorId}, endpoint {EndpointId}; category {FailureCategory}",
                monitor.Id, monitor.EndpointId, "ConfigurationDrift");
            throw new HttpConfigurationDriftException();
        }
        var http = resolvedHttp?.Policy;
        var overrides = http is null ? null : HttpMonitorConfiguration.ReadOverrides(monitor);
        var target = resolvedHttp?.Target ?? new ResolvedMonitoringTarget(monitor.EndpointId,
            monitor.Endpoint.NormalizedUrl, monitor.Endpoint.NormalizedHost, monitor.Endpoint.EffectivePort,
            monitor.Endpoint.NormalizationVersion, monitor.Endpoint.Environment.IsProduction);
        return new()
        {
            LogicalCheckId = logicalCheckId,
            SchemaVersion = 2,
            TargetNormalizedUrl = target.NormalizedUrl,
            TargetNormalizedHost = target.NormalizedHost,
            TargetEffectivePort = target.EffectivePort,
            TargetNormalizationVersion = target.NormalizationVersion,
            TargetIsProduction = target.IsProduction,
            CurrentTruthGeneration = monitor.CurrentTruthGeneration,
            MonitorType = monitor.MonitorType,
            ConfigurationFingerprint = monitor.ConfigurationFingerprint,
            IntervalSeconds = monitor.IntervalSeconds,
            TimeoutSeconds = monitor.TimeoutSeconds,
            FailureConfirmationCount = monitor.FailureConfirmationCount,
            RecoveryConfirmationCount = monitor.RecoveryConfirmationCount,
            SslWarningExpiryDays = monitor.MonitorType == SslMonitorIdentity.MonitorType ? ResolvedSslPolicy.Default.WarningExpiryDays : null,
            SslHighExpiryDays = monitor.MonitorType == SslMonitorIdentity.MonitorType ? ResolvedSslPolicy.Default.HighExpiryDays : null,
            SslCriticalExpiryDays = monitor.MonitorType == SslMonitorIdentity.MonitorType ? ResolvedSslPolicy.Default.CriticalExpiryDays : null,
            WarningThresholdMs = monitor.WarningThresholdMs,
            CriticalThresholdMs = monitor.CriticalThresholdMs,
            IntervalSource = monitor.MonitorType == SslMonitorIdentity.MonitorType ? ConfigurationValueSources.PolicyProfile
                : MonitorIntervalOverride.HasOverride(monitor.BoundedOverrides)
                ? ConfigurationValueSources.EndpointOverride
                : ConfigurationValueSources.EnvironmentDefault,
            AcceptedStatusCodes = http is null ? string.Empty : string.Join(',', http.AdditionalAcceptedStatusCodes),
            RequiredContentMarker = http?.RequiredContentMarker,
            ContentMarkerComparison = http?.ContentMarkerComparison ?? "OrdinalIgnoreCase",
            TimeoutSource = overrides?.TimeoutSeconds is not null ? ConfigurationValueSources.EndpointOverride
                : http is null ? ConfigurationValueSources.PolicyProfile : ConfigurationValueSources.SystemDefault,
            ConfirmationSource = overrides?.FailureConfirmationCount is not null || overrides?.RecoveryConfirmationCount is not null
                ? ConfigurationValueSources.EndpointOverride
                : http is null ? ConfigurationValueSources.PolicyProfile : ConfigurationValueSources.SystemDefault,
            ThresholdSource = overrides?.WarningThresholdMs is not null || overrides?.CriticalThresholdMs is not null
                ? ConfigurationValueSources.EndpointOverride
                : http is null ? ConfigurationValueSources.PolicyProfile : ConfigurationValueSources.SystemDefault,
            CreatedAt = now
        };
    }
}

internal sealed class HttpConfigurationDriftException() : InvalidOperationException("HTTP configuration drift prevents creating a check.");
