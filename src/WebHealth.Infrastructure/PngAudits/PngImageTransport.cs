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
    PngImageRequestGate imageRequestGate) : IPngImageTransport
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

        var normalized = EndpointUrlNormalizer.Normalize(request.Url);
        if (normalized.Succeeded)
        {
            await rateLimiter.WaitAsync(
                normalized.NormalizedHost!,
                request.RequestsPerSecondPerHost,
                cancellationToken);
        }

        using (await imageRequestGate.AcquireAsync(cancellationToken))
        using (await requestBudget.AcquireAsync(cancellationToken))
        {
            return await transport.SendExtendedAsync(
                new(
                    request.EndpointId,
                    request.Url,
                    request.IsProduction,
                    request.MaxRedirects,
                    request.MaxResponseBodyBytes,
                    request.TimeoutSeconds)
                {
                    HopPolicy = new RateLimitedHopPolicy(request, rateLimiter)
                },
                cancellationToken);
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
