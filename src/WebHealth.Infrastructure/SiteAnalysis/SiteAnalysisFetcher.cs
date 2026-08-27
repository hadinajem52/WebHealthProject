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
            if (attempt >= profile.TransientRetryCount
                || !SiteAnalysisRetryPolicy.IsTransient(response))
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
        var delay = SiteAnalysisRetryPolicy.Delay(
            request.ExecutionId,
            request.Url,
            attempt,
            response.RetryAfter,
            profile.RetryBaseDelay,
            profile.MaxRetryDelay);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }

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
