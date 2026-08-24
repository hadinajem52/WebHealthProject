using System.Security.Cryptography;
using System.Text;
using WebHealth.Application.Crawling;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Crawling;

internal sealed class CrawlRequestExecutor(
    CrawlRunRequest request,
    CrawlSchedulingOptions options,
    CrawlDependencies dependencies,
    TimeProvider timeProvider)
{
    public async Task<SafeHttpTransportResult> ExecuteAsync(
        string url,
        string host,
        ISafeHttpRequestHopPolicy hopPolicy,
        CancellationToken cancellationToken)
    {
        SafeHttpTransportResult result;
        for (var attempt = 0; ; attempt++)
        {
            await dependencies.RateLimiter.WaitAsync(host, cancellationToken);
            using (await dependencies.RequestBudget.AcquireAsync(cancellationToken))
            {
                result = await dependencies.Transport.SendAsync(
                    new SafeHttpTransportRequest(request.EndpointId, url, request.IsProduction,
                        MaxResponseBodyBytes: options.MaxPageBytes,
                        TimeoutSeconds: options.FetchTimeoutSeconds)
                    {
                        HopPolicy = hopPolicy
                    },
                    cancellationToken);
            }

            if (attempt >= options.TransientRetryCount || !IsTransient(result))
            {
                return result;
            }

            var delay = RetryDelay(url, attempt, result.RetryAfter);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
    }

    private TimeSpan RetryDelay(string url, int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } requested)
        {
            return requested > options.MaxRetryDelay ? options.MaxRetryDelay : requested;
        }

        var multiplier = 1 << attempt;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{request.RunId:N}:{attempt}:{url}"));
        var jitter = BitConverter.ToUInt32(bytes, 0) % 100;
        var delay = options.RetryBaseDelay * multiplier + TimeSpan.FromMilliseconds(jitter);
        return delay > options.MaxRetryDelay ? options.MaxRetryDelay : delay;
    }

    private static bool IsTransient(SafeHttpTransportResult result) =>
        result.StatusCode is 408 or 425 or 429 or >= 500
        || result.Failure is SafeHttpFailureKind.NameResolution
            or SafeHttpFailureKind.Connection
            or SafeHttpFailureKind.Tls
            or SafeHttpFailureKind.Timeout
            or SafeHttpFailureKind.ResponseHeadersTooLarge
            or SafeHttpFailureKind.Protocol;
}
