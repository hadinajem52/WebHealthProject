using System.Security.Cryptography;
using System.Text;

namespace WebHealth.Infrastructure.Registry;

internal static class RegistryDefaults
{
    public static readonly Guid HttpAvailabilityPolicyProfileId =
        new("fd3c8021-ff54-4f31-a3ad-2010b7b193dd");

    public const string HttpAvailabilityMonitorType = "HttpAvailability";
    public const int HttpTimeoutSeconds = 30;
    public static readonly DateTimeOffset SeedTimestamp = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

    public static int GetHttpIntervalSeconds(bool isProduction) => isProduction ? 300 : 900;

    public static string CreateHttpFingerprint(string normalizedUrl, int intervalSeconds, int timeoutSeconds) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"v1|{normalizedUrl}|{HttpAvailabilityMonitorType}|{intervalSeconds}|{timeoutSeconds}")))
            .ToLowerInvariant();
}
