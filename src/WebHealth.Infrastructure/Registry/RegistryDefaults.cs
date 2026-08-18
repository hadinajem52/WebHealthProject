using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Infrastructure.Registry;

internal static class RegistryDefaults
{
    public static readonly Guid HttpAvailabilityPolicyProfileId =
        new("fd3c8021-ff54-4f31-a3ad-2010b7b193dd");

    public static readonly Guid SslCertificatePolicyProfileId =
        new("0d6d3f5c-4a1b-4d2e-9f30-6b8c5a2d71e4");

    public const string HttpAvailabilityMonitorType = HttpIssueIdentity.MonitorType;
    public const string SslCertificateMonitorType = SslMonitorIdentity.MonitorType;
    public const int HttpTimeoutSeconds = 30;

    /// <summary>BR-C07: SSL certificates are checked once a day by default.</summary>
    public const int SslIntervalSeconds = 24 * 60 * 60;
    public const int SslTimeoutSeconds = 15;

    /// <summary>
    /// A certificate problem is a single confirmed observation: unlike a flapping HTTP
    /// response, an expired or untrusted certificate does not resolve itself between daily
    /// checks, and waiting a second day to confirm would waste a day of the expiry window.
    /// </summary>
    public const int SslFailureConfirmationCount = 1;
    public const int SslRecoveryConfirmationCount = 1;
    public static readonly DateTimeOffset SeedTimestamp = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

    public static int GetHttpIntervalSeconds(bool isProduction) =>
        MonitorCadence.GetDefaultIntervalSeconds(isProduction);

    /// <summary>BR-C01: only HTTPS endpoints have a certificate to monitor.</summary>
    public static bool RequiresSslMonitor(string normalizedUrl) =>
        normalizedUrl.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.Ordinal);

    public static string CreateSslFingerprint(string normalizedUrl, bool isProduction) =>
        HttpPolicyFingerprint.Create(new(
            normalizedUrl,
            SslCertificateMonitorType,
            isProduction,
            SslIntervalSeconds,
            SslTimeoutSeconds,
            SslFailureConfirmationCount,
            SslRecoveryConfirmationCount,
            null,
            null,
            [],
            null,
            "OrdinalIgnoreCase",
            FindingSeverities.Warning,
            SafeHttpTransportDefaults.MaxDecodedBodyBytes,
            SafeHttpTransportDefaults.MaxRedirects));

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
