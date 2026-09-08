using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.PageAudits;

namespace WebHealth.Infrastructure.Registry;

internal static class RegistryDefaults
{
    public static readonly Guid HttpAvailabilityPolicyProfileId =
        new("fd3c8021-ff54-4f31-a3ad-2010b7b193dd");

    public static readonly Guid SslCertificatePolicyProfileId =
        new("0d6d3f5c-4a1b-4d2e-9f30-6b8c5a2d71e4");

    public static readonly Guid PageAuditPolicyProfileId =
        new("624bbbda-96d1-46f8-8382-16686d3f400e");

    public const string HttpAvailabilityMonitorType = HttpIssueIdentity.MonitorType;
    public const string SslCertificateMonitorType = SslMonitorIdentity.MonitorType;
    public const string PageAuditMonitorType = PageAuditMonitorIdentity.MonitorType;
    public const int HttpTimeoutSeconds = 15;

    public const int SslIntervalSeconds = 24 * 60 * 60;
    public const int SslTimeoutSeconds = 15;

    public const int SslFailureConfirmationCount = 1;
    public const int SslRecoveryConfirmationCount = 1;
    public const int PageAuditIntervalSeconds = 24 * 60 * 60;
    public const int PageAuditTimeoutSeconds = 90;
    public const int PageAuditFailureConfirmationCount = 1;
    public const int PageAuditRecoveryConfirmationCount = 1;
    public static readonly DateTimeOffset SeedTimestamp = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

    public static int GetHttpIntervalSeconds(bool isProduction) =>
        MonitorCadence.GetDefaultIntervalSeconds(isProduction);

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
            SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes,
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
            SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes,
            SafeHttpTransportDefaults.MaxRedirects));

    public static string CreatePageAuditFingerprint(string normalizedUrl, bool isProduction) =>
        HttpPolicyFingerprint.Create(new(
            normalizedUrl,
            PageAuditMonitorType,
            isProduction,
            PageAuditIntervalSeconds,
            PageAuditTimeoutSeconds,
            PageAuditFailureConfirmationCount,
            PageAuditRecoveryConfirmationCount,
            null,
            null,
            [],
            null,
            "OrdinalIgnoreCase",
            FindingSeverities.Warning,
            SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes,
            SafeHttpTransportDefaults.MaxRedirects));
}
