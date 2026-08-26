using System.Security.Cryptography;
using System.Text;
using WebHealth.Application.Monitoring;
using WebHealth.Application.SiteAnalysis;

namespace WebHealth.Infrastructure.SiteAnalysis;

internal sealed class SiteAnalysisFetcher(
    ISafeHttpTransport transport,
    SiteAnalysisRequestBudget requestBudget,
    SiteAnalysisHostRateLimiter rateLimiter,
    TimeProvider timeProvider) : ISiteAnalysisFetcher
{
    public async Task<SiteAnalysisFetchResult> FetchAsync(
        SiteAnalysisFetchRequest request,
        SiteAnalysisFetchProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(profile);
        if (request.MaxOutboundRequests <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        SafeHttpTransportResult response;
        var outboundRequestCount = 0;
        for (var attempt = 0; ; attempt++)
        {
            var remainingRequests = request.MaxOutboundRequests - outboundRequestCount;
            response = await SendAsync(request, profile, remainingRequests, cancellationToken);
            outboundRequestCount = checked(outboundRequestCount + response.OutboundRequestCount);
            var redirectBudgetReached = response.Failure == SafeHttpFailureKind.RedirectLimit
                && remainingRequests <= SafeHttpTransportDefaults.MaxRedirects;
            if (attempt >= profile.TransientRetryCount || !IsTransient(response))
            {
                return new(response, attempt + 1, outboundRequestCount, redirectBudgetReached);
            }
            if (outboundRequestCount >= request.MaxOutboundRequests)
            {
                return new(response, attempt + 1, outboundRequestCount, true);
            }

            await WaitBeforeRetryAsync(request, profile, response, attempt, cancellationToken);
        }
    }

    private async Task<SafeHttpTransportResult> SendAsync(
        SiteAnalysisFetchRequest request,
        SiteAnalysisFetchProfile profile,
        int remainingRequests,
        CancellationToken cancellationToken)
    {
        await rateLimiter.WaitAsync(
            request.Host,
            profile.RequestsPerSecondPerHost,
            cancellationToken);
        using (await requestBudget.AcquireAsync(cancellationToken))
        {
            return await transport.SendAsync(
                new SafeHttpTransportRequest(
                    request.EndpointId,
                    request.Url,
                    request.IsProduction,
                    MaxRedirects: Math.Min(
                        SafeHttpTransportDefaults.MaxRedirects,
                        remainingRequests - 1),
                    MaxResponseBodyBytes: profile.MaxResponseBodyBytes,
                    TimeoutSeconds: profile.TimeoutSeconds)
                {
                    HopPolicy = new RateLimitedHopPolicy(request, profile, rateLimiter)
                },
                cancellationToken);
        }
    }

    private async Task WaitBeforeRetryAsync(
        SiteAnalysisFetchRequest request,
        SiteAnalysisFetchProfile profile,
        SafeHttpTransportResult response,
        int attempt,
        CancellationToken cancellationToken)
    {
        var delay = RetryDelay(request, profile, response.RetryAfter, attempt);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }

    private static TimeSpan RetryDelay(
        SiteAnalysisFetchRequest request,
        SiteAnalysisFetchProfile profile,
        TimeSpan? retryAfter,
        int attempt)
    {
        if (retryAfter is { } requested)
        {
            return Min(requested, profile.MaxRetryDelay);
        }

        var multiplier = 1 << attempt;
        var jitter = RetryJitter(request, attempt);
        return Min(profile.RetryBaseDelay * multiplier + jitter, profile.MaxRetryDelay);
    }

    private static TimeSpan RetryJitter(SiteAnalysisFetchRequest request, int attempt)
    {
        var value = $"{request.ExecutionId:N}:{attempt}:{request.Url}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return TimeSpan.FromMilliseconds(BitConverter.ToUInt32(bytes, 0) % 100);
    }

    private static TimeSpan Min(TimeSpan value, TimeSpan maximum) =>
        value > maximum ? maximum : value;

    private static bool IsTransient(SafeHttpTransportResult result) =>
        result.StatusCode is 408 or 425 or 429 or >= 500
        || result.Failure is SafeHttpFailureKind.NameResolution
            or SafeHttpFailureKind.Connection
            or SafeHttpFailureKind.Tls
            or SafeHttpFailureKind.Timeout
            or SafeHttpFailureKind.ResponseHeadersTooLarge
            or SafeHttpFailureKind.Protocol;

    private sealed class RateLimitedHopPolicy(
        SiteAnalysisFetchRequest request,
        SiteAnalysisFetchProfile profile,
        SiteAnalysisHostRateLimiter rateLimiter) : ISafeHttpRequestHopPolicy
    {
        public async Task<SafeHttpRequestHopDecision> EvaluateAsync(
            SafeHttpRequestHop hop,
            CancellationToken cancellationToken = default)
        {
            var decision = request.HopPolicy is null
                ? new SafeHttpRequestHopDecision(true)
                : await request.HopPolicy.EvaluateAsync(hop, cancellationToken);
            if (!decision.Allowed || hop.RedirectCount == 0) return decision;

            var host = new Uri(hop.Url, UriKind.Absolute).IdnHost;
            await rateLimiter.WaitAsync(
                host,
                profile.RequestsPerSecondPerHost,
                cancellationToken);
            return decision;
        }
    }
}
