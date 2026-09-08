using System.Text.Json;
using System.Text.Json.Serialization;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

internal static class HttpMonitorConfiguration
{
    private static readonly IHttpMonitoringPolicyResolver Resolver = new HttpMonitoringPolicyResolver();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static HttpMonitorOverridesV2 ReadOverrides(EndpointMonitor monitor) =>
        ReadOverrides(monitor.BoundedOverrides, monitor.TimeoutSeconds, monitor.FailureConfirmationCount,
            monitor.RecoveryConfirmationCount, monitor.WarningThresholdMs, monitor.CriticalThresholdMs);

    public static HttpMonitorOverridesV2 ReadOverrides(string settings, int timeout, int failures, int recoveries, int? warning, int? critical)
    {
        using var document = JsonDocument.Parse(settings);
        if (document.RootElement.TryGetProperty("schemaVersion", out _))
            return JsonSerializer.Deserialize<HttpMonitorOverridesV2>(settings, JsonOptions)
                ?? throw new InvalidOperationException("The HTTP policy is missing.");
        if (document.RootElement.EnumerateObject().Any(property => property.Name != "intervalSeconds"))
            throw new InvalidOperationException("The legacy HTTP policy contains unsupported fields.");
        return new()
        {
            IntervalSeconds = MonitorIntervalOverride.GetSeconds(settings),
            TimeoutSeconds = timeout,
            FailureConfirmationCount = failures,
            RecoveryConfirmationCount = recoveries,
            WarningThresholdMs = warning,
            CriticalThresholdMs = critical
        };
    }

    public static HttpMonitoringPolicyResolution Resolve(HttpMonitorOverridesV2 overrides, bool isProduction) =>
        Resolver.Resolve(overrides, isProduction);

    public static ResolvedHttpMonitoringPolicy ReadEffective(EndpointMonitor monitor, bool isProduction)
    {
        var resolution = Resolve(ReadOverrides(monitor), isProduction);
        return resolution.Policy ?? throw new InvalidOperationException("The HTTP policy is invalid.");
    }

    public static string Fingerprint(string url, bool isProduction, ResolvedHttpMonitoringPolicy policy) =>
        HttpPolicyFingerprint.Create(new(url, RegistryDefaults.HttpAvailabilityMonitorType, isProduction,
            policy.IntervalSeconds, policy.TimeoutSeconds, policy.FailureConfirmationCount,
            policy.RecoveryConfirmationCount, policy.WarningThresholdMs, policy.CriticalThresholdMs,
            policy.AdditionalAcceptedStatusCodes, policy.RequiredContentMarker, policy.ContentMarkerComparison,
            FindingSeverities.Warning, SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes,
            SafeHttpTransportDefaults.MaxRedirects));

    public static void Apply(EndpointMonitor monitor, string url, bool isProduction, HttpMonitorOverridesV2 overrides)
    {
        var resolution = Resolve(overrides, isProduction);
        var policy = resolution.Policy ?? throw new InvalidOperationException("The HTTP policy is invalid.");
        monitor.IntervalSeconds = policy.IntervalSeconds;
        monitor.TimeoutSeconds = policy.TimeoutSeconds;
        monitor.FailureConfirmationCount = policy.FailureConfirmationCount;
        monitor.RecoveryConfirmationCount = policy.RecoveryConfirmationCount;
        monitor.WarningThresholdMs = policy.WarningThresholdMs;
        monitor.CriticalThresholdMs = policy.CriticalThresholdMs;
        monitor.BoundedOverrides = JsonSerializer.Serialize(overrides with
        {
            AdditionalAcceptedStatusCodes = policy.AdditionalAcceptedStatusCodes.Count == 0 ? null : policy.AdditionalAcceptedStatusCodes,
            RequiredContentMarker = policy.RequiredContentMarker
        }, JsonOptions);
        monitor.ConfigurationFingerprint = Fingerprint(url, isProduction, policy);
    }

    public static ResolvedHttpCheckConfiguration ResolveCheck(EndpointMonitor monitor)
    {
        var endpoint = monitor.Endpoint;
        var policy = RequireConsistent(monitor, endpoint.Environment.IsProduction);
        return new(new(endpoint.Id, endpoint.NormalizedUrl, endpoint.NormalizedHost, endpoint.EffectivePort,
            endpoint.NormalizationVersion, endpoint.Environment.IsProduction), policy,
            monitor.ConfigurationFingerprint, monitor.CurrentTruthGeneration);
    }

    public static ResolvedHttpMonitoringPolicy RequireConsistent(EndpointMonitor monitor, bool isProduction)
    {
        using var document = JsonDocument.Parse(monitor.BoundedOverrides);
        if (!document.RootElement.TryGetProperty("schemaVersion", out _))
        {
            ReadOverrides(monitor);
            return new(monitor.IntervalSeconds, monitor.TimeoutSeconds, monitor.FailureConfirmationCount,
                monitor.RecoveryConfirmationCount, monitor.WarningThresholdMs ?? 1500,
                monitor.CriticalThresholdMs ?? 3000, [], null, "OrdinalIgnoreCase");
        }
        var policy = ReadEffective(monitor, isProduction);
        if (monitor.IntervalSeconds != policy.IntervalSeconds || monitor.TimeoutSeconds != policy.TimeoutSeconds
            || monitor.FailureConfirmationCount != policy.FailureConfirmationCount
            || monitor.RecoveryConfirmationCount != policy.RecoveryConfirmationCount
            || monitor.WarningThresholdMs != policy.WarningThresholdMs || monitor.CriticalThresholdMs != policy.CriticalThresholdMs
            || monitor.ConfigurationFingerprint != Fingerprint(monitor.Endpoint.NormalizedUrl, isProduction, policy))
            throw new InvalidOperationException("The HTTP policy does not match its materialized configuration.");
        return policy;
    }
}
