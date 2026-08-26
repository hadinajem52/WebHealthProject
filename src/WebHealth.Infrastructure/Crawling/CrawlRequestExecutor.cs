using WebHealth.Application.Crawling;
using WebHealth.Application.Monitoring;
using WebHealth.Application.SiteAnalysis;

namespace WebHealth.Infrastructure.Crawling;

internal sealed class CrawlRequestExecutor
{
    private readonly CrawlRunRequest _request;
    private readonly ISiteAnalysisFetcher _fetcher;
    private readonly SiteAnalysisFetchProfile _profile;

    public CrawlRequestExecutor(
        CrawlRunRequest request,
        CrawlSchedulingOptions options,
        ISiteAnalysisFetcher fetcher)
    {
        _request = request;
        _fetcher = fetcher;
        _profile = new(
            options.MaxPageBytes,
            options.FetchTimeoutSeconds,
            options.RequestsPerSecondPerHost,
            options.TransientRetryCount,
            options.RetryBaseDelay,
            options.MaxRetryDelay);
    }

    public async Task<SafeHttpTransportResult> ExecuteAsync(
        string url,
        ISafeHttpRequestHopPolicy hopPolicy,
        CancellationToken cancellationToken)
    {
        var result = await _fetcher.FetchAsync(
            new(
                _request.RunId,
                _request.EndpointId,
                url,
                _request.IsProduction)
            {
                HopPolicy = hopPolicy
            },
            _profile,
            cancellationToken);
        return result.Response;
    }
}
