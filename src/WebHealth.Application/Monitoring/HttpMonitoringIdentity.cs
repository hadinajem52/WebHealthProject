using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WebHealth.Domain.Normalization;

namespace WebHealth.Application.Monitoring;

public static class SafeHttpRequestIdentity
{
    public static string? Create(SafeHttpTransportRequest request)
    {
        var normalized = EndpointUrlNormalizer.Normalize(request.Url);
        if (!normalized.Succeeded)
        {
            return null;
        }

        var canonical = new StringBuilder("v1|");
        Append(canonical, request.EndpointId.ToString("N"));
        Append(canonical, normalized.NormalizedUrl!);
        Append(canonical, request.IsProduction ? "1" : "0");
        Append(canonical, request.MaxRedirects.ToString(CultureInfo.InvariantCulture));
        Append(canonical, request.MaxResponseBodyBytes.ToString(CultureInfo.InvariantCulture));
        Append(canonical, request.TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        return Hash(canonical);
    }

    private static void Append(StringBuilder target, string value) =>
        target.Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('|');

    private static string Hash(StringBuilder canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
}

public sealed record HttpPolicyFingerprintInput(
    string NormalizedUrl,
    string MonitorType,
    bool IsProduction,
    int IntervalSeconds,
    int TimeoutSeconds,
    int FailureConfirmationCount,
    int RecoveryConfirmationCount,
    int? WarningThresholdMs,
    int? CriticalThresholdMs,
    IReadOnlyCollection<int> AcceptedStatusCodes,
    string? RequiredContentMarker,
    string ContentMarkerComparison,
    string ProductionHttpSeverity,
    int MaxResponseBodyBytes,
    int MaxRedirects);

public static class HttpPolicyFingerprint
{
    public static string Create(HttpPolicyFingerprintInput input)
    {
        var statuses = string.Join(',', input.AcceptedStatusCodes
            .Distinct()
            .Order()
            .Select(status => status.ToString(CultureInfo.InvariantCulture)));
        var canonical = new StringBuilder("v2|");
        Append(canonical, input.NormalizedUrl);
        Append(canonical, input.MonitorType);
        Append(canonical, input.IsProduction ? "1" : "0");
        Append(canonical, input.IntervalSeconds);
        Append(canonical, input.TimeoutSeconds);
        Append(canonical, input.FailureConfirmationCount);
        Append(canonical, input.RecoveryConfirmationCount);
        Append(canonical, input.WarningThresholdMs);
        Append(canonical, input.CriticalThresholdMs);
        Append(canonical, statuses);
        Append(canonical, input.RequiredContentMarker);
        Append(canonical, input.ContentMarkerComparison);
        Append(canonical, input.ProductionHttpSeverity);
        Append(canonical, input.MaxResponseBodyBytes);
        Append(canonical, input.MaxRedirects);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(StringBuilder target, int value) =>
        Append(target, value.ToString(CultureInfo.InvariantCulture));

    private static void Append(StringBuilder target, int? value) =>
        Append(target, value?.ToString(CultureInfo.InvariantCulture));

    private static void Append(StringBuilder target, string? value)
    {
        if (value is null)
        {
            target.Append("-1:|");
            return;
        }

        target.Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('|');
    }
}

public static class HttpIssueIdentity
{
    public const string MonitorType = "HttpAvailability";
    public const string DefaultDiscriminator = "default";

    public static string Create(string ruleKey, string discriminator = DefaultDiscriminator) =>
        $"v1|{MonitorType}|{ruleKey}|{discriminator}";
}
