using System.Security.Cryptography;
using System.Text;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.SiteAnalysis;

internal static class SiteAnalysisRetryPolicy
{
    public static bool IsTransient(SafeHttpTransportResult result) =>
        result.StatusCode is 408 or 425 or 429 or >= 500
        || result.Failure is SafeHttpFailureKind.NameResolution
            or SafeHttpFailureKind.Connection
            or SafeHttpFailureKind.Tls
            or SafeHttpFailureKind.Timeout
            or SafeHttpFailureKind.ResponseHeadersTooLarge
            or SafeHttpFailureKind.Protocol;

    public static TimeSpan Delay(
        Guid executionId,
        string url,
        int attempt,
        TimeSpan? retryAfter,
        TimeSpan baseDelay,
        TimeSpan maxDelay)
    {
        if (retryAfter is { } requested)
        {
            return Min(requested, maxDelay);
        }

        var multiplier = 1 << attempt;
        return Min(baseDelay * multiplier + Jitter(executionId, url, attempt), maxDelay);
    }

    private static TimeSpan Jitter(Guid executionId, string url, int attempt)
    {
        var value = $"{executionId:N}:{attempt}:{url}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return TimeSpan.FromMilliseconds(BitConverter.ToUInt32(bytes, 0) % 100);
    }

    private static TimeSpan Min(TimeSpan value, TimeSpan maximum) =>
        value > maximum ? maximum : value;
}
