using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Infrastructure.Registry;

internal static class RegistryDefaults
{
    public static readonly Guid HttpAvailabilityPolicyProfileId =
        new("fd3c8021-ff54-4f31-a3ad-2010b7b193dd");

    public const string HttpAvailabilityMonitorType = HttpIssueIdentity.MonitorType;
    public const int HttpTimeoutSeconds = 30;
    public static readonly DateTimeOffset SeedTimestamp = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

    public static int GetHttpIntervalSeconds(bool isProduction) =>
        MonitorCadence.GetDefaultIntervalSeconds(isProduction);

    public static string CreateHttpFingerprint(
        string normalizedUrl,
        bool isProduction,
        int intervalSeconds,
        int timeoutSeconds,
        int failureConfirmationCount,
        int recoveryConfirmationCount,
        int? warningThresholdMs,
        int? criticalThresholdMs) =>
        HttpPolicyFingerprint.Create(new(
            normalizedUrl,
            HttpAvailabilityMonitorType,
            isProduction,
            intervalSeconds,
            timeoutSeconds,
            failureConfirmationCount,
            recoveryConfirmationCount,
            warningThresholdMs,
            criticalThresholdMs,
            [],
            null,
            "OrdinalIgnoreCase",
            FindingSeverities.Warning,
            SafeHttpTransportDefaults.MaxDecodedBodyBytes,
            SafeHttpTransportDefaults.MaxRedirects));
}
