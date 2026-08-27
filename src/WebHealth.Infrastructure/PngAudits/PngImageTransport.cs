using WebHealth.Application.Monitoring;
using WebHealth.Application.PngAudits;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.SiteAnalysis;

namespace WebHealth.Infrastructure.PngAudits;

internal sealed class PngImageTransport(
    SafeHttpTransport transport,
    SiteAnalysisRequestBudget requestBudget,
    SiteAnalysisHostRateLimiter rateLimiter,
    PngImageRequestGate imageRequestGate,
    TimeProvider timeProvider) : IPngImageTransport
{
    public async Task<SafeHttpTransportResult> SendAsync(
        PngImageTransportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!double.IsFinite(request.RequestsPerSecondPerHost)
            || request.RequestsPerSecondPerHost is <= 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        if (request.TransientRetryCount < 0 || request.MaxOutboundRequests < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var normalized = EndpointUrlNormalizer.Normalize(request.Url);
        var host = normalized.Succeeded ? normalized.NormalizedHost : null;

        using (await imageRequestGate.AcquireAsync(cancellationToken))
        {
            var outboundRequestCount = 0;
            for (var attempt = 0; ; attempt++)
            {
                var remainingRequests = request.MaxOutboundRequests - outboundRequestCount;
                if (host is not null)
                {
                    await rateLimiter.WaitAsync(
                        host,
                        request.RequestsPerSecondPerHost,
                        cancellationToken);
                }

                SafeHttpTransportResult response;
                using (await requestBudget.AcquireAsync(cancellationToken))
                {
                    response = await transport.SendExtendedAsync(
                        new(
                            request.EndpointId,
                            request.Url,
                            request.IsProduction,
                            Math.Min(request.MaxRedirects, remainingRequests - 1),
                            request.MaxResponseBodyBytes,
                            request.TimeoutSeconds)
                        {
                            HopPolicy = new RateLimitedHopPolicy(request, rateLimiter)
                        },
                        cancellationToken);
                }

                outboundRequestCount = checked(outboundRequestCount + response.OutboundRequestCount);
                if (attempt >= request.TransientRetryCount
                    || outboundRequestCount >= request.MaxOutboundRequests
                    || !SiteAnalysisRetryPolicy.IsTransient(response))
                {
                    return response with { OutboundRequestCount = outboundRequestCount };
                }

                var delay = SiteAnalysisRetryPolicy.Delay(
                    request.EndpointId,
                    request.Url,
                    attempt,
                    response.RetryAfter,
                    PngAuditFetchRetry.BaseDelay,
                    PngAuditFetchRetry.MaxDelay);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, timeProvider, cancellationToken);
                }
            }
        }
    }

    private sealed class RateLimitedHopPolicy(
        PngImageTransportRequest request,
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
                request.RequestsPerSecondPerHost,
                cancellationToken);
            return decision;
        }
    }
}
