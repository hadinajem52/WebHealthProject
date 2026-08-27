using WebHealth.Application.Crawling;
using WebHealth.Application.Monitoring;
using WebHealth.Application.PngAudits;
using WebHealth.Application.SiteAnalysis;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.Infrastructure.PngAudits;

internal sealed class PngSiteCrawler(
    ISiteAnalysisFetcher fetcher,
    IHtmlDocumentDiscoveryExtractor discoveryExtractor,
    ICrawlRobotsReader robotsReader,
    SafeHttpTransportOptions transportOptions,
    TimeProvider timeProvider) : IPngSiteCrawler
{
    public Task<PngSiteDiscoveryResult> DiscoverAsync(
        PngSiteCrawlRequest request,
        CancellationToken cancellationToken = default) =>
        new PngSiteDiscoveryExecution(
            request,
            fetcher,
            discoveryExtractor,
            robotsReader,
            transportOptions.UserAgentHeader,
            timeProvider).RunAsync(cancellationToken);
}

internal sealed class PngSiteDiscoveryExecution
{
    private readonly PngSiteCrawlRequest _request;
    private readonly ISiteAnalysisFetcher _fetcher;
    private readonly IHtmlDocumentDiscoveryExtractor _discoveryExtractor;
    private readonly ICrawlRobotsReader _robotsReader;
    private readonly string _userAgent;
    private readonly TimeProvider _timeProvider;
    private readonly CrawlFrontier _frontier;
    private readonly PngAssetScope _assetScope;
    private readonly PngImageLedger _imageLedger;
    private readonly DateTimeOffset _deadline;
    private readonly Dictionary<string, CrawlRobotsFacts> _robotsByOrigin = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inspectedPageIdentities = new(StringComparer.Ordinal);
    private readonly List<PngDiscoveredPage> _pages = [];
    private readonly List<PngDiscoverySkip> _skips = [];
    private readonly Dictionary<(PngCoverageArea Area, PngCoverageReasonCode Reason), int> _coverage = [];

    private int _httpAttempts;
    private long _totalPageBytes;
    private bool _stop;

    public PngSiteDiscoveryExecution(
        PngSiteCrawlRequest request,
        ISiteAnalysisFetcher fetcher,
        IHtmlDocumentDiscoveryExtractor discoveryExtractor,
        ICrawlRobotsReader robotsReader,
        string userAgent,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        _fetcher = fetcher;
        _discoveryExtractor = discoveryExtractor;
        _robotsReader = robotsReader;
        _userAgent = userAgent;
        _timeProvider = timeProvider;
        _httpAttempts = Math.Max(0, request.ConsumedHttpAttempts);
        _totalPageBytes = Math.Max(0, request.ConsumedPageBytes);

        var seed = CrawlUrlNormalizer.Normalize(request.Scope.SeedUrl, request.Scope.UrlOptions).Url
            ?? throw new ArgumentException("The PNG discovery seed URL is invalid.", nameof(request));
        var pageScope = new CrawlScope(
            [seed],
            request.Scope.AllowedPageHosts,
            request.Scope.AllowedPagePathPrefixes);
        if (pageScope.Validate().Count > 0)
        {
            throw new ArgumentException("The PNG discovery seed URL is outside the page scope.", nameof(request));
        }

        _frontier = new(pageScope, new CrawlLimits
        {
            MaxPages = request.Profile.Pages.MaxPages,
            MaxCheckOnlyRequests = 1,
            MaxDepth = request.Profile.Pages.MaxDepth
        });
        _assetScope = new(request.Scope.AllowedAssetHosts);
        _imageLedger = new(request.Profile.Images);
        _deadline = request.RunDeadline
            ?? timeProvider.GetUtcNow() + request.Profile.Fetch.MaxDuration;
    }

    public async Task<PngSiteDiscoveryResult> RunAsync(CancellationToken cancellationToken)
    {
        var remainingDuration = _deadline - _timeProvider.GetUtcNow();
        using var deadlineCancellation = new CancellationTokenSource(
            remainingDuration > TimeSpan.Zero ? remainingDuration : TimeSpan.Zero,
            _timeProvider);
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadlineCancellation.Token);
        try
        {
            await RunCoreAsync(runCancellation.Token);
        }
        catch (OperationCanceledException) when (
            deadlineCancellation.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            AddCoverageIfMissing(PngCoverageArea.Crawl, PngCoverageReasonCode.DurationLimit);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (deadlineCancellation.IsCancellationRequested)
        {
            AddCoverageIfMissing(PngCoverageArea.Crawl, PngCoverageReasonCode.DurationLimit);
        }

        return Result();
    }

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        while (!_stop && _frontier.TryDequeue(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanStartRequest()) break;
            if (!await IsRobotsAllowedAsync(item.Url, cancellationToken))
            {
                AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.RobotsDisallowed);
                continue;
            }

            await VisitAsync(item, cancellationToken);
        }
    }

    private PngSiteDiscoveryResult Result() =>
        new(
            _pages,
            _imageLedger.Images,
            _imageLedger.SourceMappings,
            _skips,
            [.. _coverage
                .OrderBy(item => item.Key.Area)
                .ThenBy(item => item.Key.Reason)
                .Select(item => new PngCoverageReason(item.Key.Area, item.Key.Reason, item.Value))],
            _httpAttempts,
            _totalPageBytes);

    private bool CanStartRequest()
    {
        if (_timeProvider.GetUtcNow() >= _deadline)
        {
            AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.DurationLimit);
            return false;
        }
        if (_httpAttempts >= _request.Profile.Fetch.MaxTotalHttpAttempts)
        {
            AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.HttpAttemptLimit);
            return false;
        }
        if (_totalPageBytes >= _request.Profile.Pages.MaxTotalPageBytes)
        {
            AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.TotalPageBytesLimit);
            return false;
        }

        return true;
    }

    private async Task VisitAsync(CrawlWorkItem item, CancellationToken cancellationToken)
    {
        var fetch = await FetchAsync(item.Url, cancellationToken);
        _httpAttempts = checked(_httpAttempts + fetch.OutboundRequestCount);
        _totalPageBytes += fetch.Response.ResponseBytesRead;
        if (fetch.OutboundRequestLimitReached)
        {
            AddCoverageIfMissing(PngCoverageArea.Crawl, PngCoverageReasonCode.HttpAttemptLimit);
            _stop = true;
        }
        if (!fetch.Response.Succeeded)
        {
            if (fetch.Response.Failure != SafeHttpFailureKind.RequestPolicyRejected)
            {
                AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.PageFetchFailed);
            }
            return;
        }
        if (fetch.Response.BodyTruncated)
        {
            HandleTruncatedBody();
            return;
        }
        if (fetch.Response.StatusCode is not (>= 200 and <= 299))
        {
            AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.PageHttpNonSuccess);
            return;
        }
        if (DocumentUrl(item.Url, fetch.Response) is not { } document) return;

        var page = Page(document, item.Depth);
        if (!_inspectedPageIdentities.Add(page.IdentityHash)) return;
        _pages.Add(page);

        var discovery = _discoveryExtractor.Extract(
            fetch.Response.Body,
            fetch.Response.ContentType);
        TrackDocumentCoverage(discovery);
        DiscoverImages(page, document, discovery);
        DiscoverPages(document, item.Depth, discovery);
    }

    private async Task<SiteAnalysisFetchResult> FetchAsync(
        CrawlUrl url,
        CancellationToken cancellationToken)
    {
        var remainingAttempts = _request.Profile.Fetch.MaxTotalHttpAttempts - _httpAttempts;
        var remainingBytes = _request.Profile.Pages.MaxTotalPageBytes - _totalPageBytes;
        var maxPageBytes = (int)Math.Min(_request.Profile.Pages.MaxPageBytes, remainingBytes);
        var retries = Math.Min(
            _request.Profile.Fetch.TransientRetryCount,
            remainingAttempts - 1);
        var profile = new SiteAnalysisFetchProfile(
            maxPageBytes,
            _request.Profile.Fetch.TimeoutSeconds,
            _request.Profile.Fetch.RequestsPerSecondPerHost,
            retries,
            PngAuditFetchRetry.BaseDelay,
            PngAuditFetchRetry.MaxDelay);
        return await _fetcher.FetchAsync(
            new(_request.RunId, _request.EndpointId, url.Value, _request.IsProduction)
            {
                HopPolicy = new PageRedirectHopPolicy(this),
                MaxOutboundRequests = remainingAttempts
            },
            profile,
            cancellationToken);
    }

    private void HandleTruncatedBody()
    {
        if (_totalPageBytes >= _request.Profile.Pages.MaxTotalPageBytes)
        {
            AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.TotalPageBytesLimit);
            _stop = true;
            return;
        }

        AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.PageBodyTruncated);
    }

    private CrawlUrl? DocumentUrl(CrawlUrl requested, SafeHttpTransportResult response)
    {
        if (response.Redirects.Count == 0)
        {
            return requested;
        }
        var finalUrl = response.FinalRequestUrl;
        var normalized = CrawlUrlNormalizer.Normalize(finalUrl, _request.Scope.UrlOptions).Url;
        if (normalized is not null
            && _frontier.Scope.Decide(normalized) == CrawlScopeDecision.Internal)
        {
            return normalized;
        }

        AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.RedirectOutOfScope);
        return null;
    }

    private PngDiscoveredPage Page(CrawlUrl document, int depth)
    {
        var displayUrl = CrawlUrlRedactor.Redact(document.Value, _request.Scope.UrlOptions)
            ?? string.Empty;
        return new(displayUrl, PngAssetUrlResolver.IdentityHash(document.Value), depth);
    }

    private void TrackDocumentCoverage(HtmlDocumentDiscovery discovery)
    {
        if (ReferenceEquals(discovery, HtmlDocumentDiscovery.NotInspected))
        {
            AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.DocumentNotInspected);
            AddCoverage(PngCoverageArea.ImageAnalysis, PngCoverageReasonCode.DocumentNotInspected);
            return;
        }
        if (!discovery.NavigationFullyInspected)
        {
            AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.NavigationReferenceLimit);
        }
        if (!discovery.ImageReferencesFullyInspected)
        {
            AddCoverage(PngCoverageArea.ImageAnalysis, PngCoverageReasonCode.ImageReferenceLimit);
        }
    }

    private void DiscoverImages(
        PngDiscoveredPage page,
        CrawlUrl document,
        HtmlDocumentDiscovery discovery)
    {
        var limit = _request.Profile.Pages.MaxImageReferencesPerPage;
        if (discovery.Images.Count > limit)
        {
            AddCoverage(
                PngCoverageArea.ImageAnalysis,
                PngCoverageReasonCode.ImageReferenceLimit,
                discovery.Images.Count - limit);
            AddSkip(page, discovery.Images[limit], PngDiscoverySkipReason.ReferenceLimit);
        }

        var resolutionBase = PngAssetUrlResolver.ResolutionBase(document.Value, discovery.BaseHref);
        foreach (var reference in discovery.Images.Take(limit))
        {
            DiscoverImage(page, reference, resolutionBase);
        }
    }

    private void DiscoverImage(
        PngDiscoveredPage page,
        HtmlImageReference reference,
        Uri resolutionBase)
    {
        var resolution = PngAssetUrlResolver.Resolve(
            reference.RawUrl,
            resolutionBase,
            _request.Scope.UrlOptions);
        if (resolution.Asset is not { } asset)
        {
            AddSkip(page, reference, resolution.Rejection!.Value);
            return;
        }
        if (!_assetScope.Allows(asset.Host))
        {
            AddSkip(page, reference, PngDiscoverySkipReason.ExternalAssetHost);
            return;
        }

        var admission = _imageLedger.Add(asset, page, reference);
        if (admission == PngImageLedgerAdmission.UniqueImageLimit)
        {
            AddCoverage(PngCoverageArea.ImageAnalysis, PngCoverageReasonCode.UniqueImageLimit);
            AddSkip(page, reference, PngDiscoverySkipReason.UniqueImageLimit);
        }
        else if (admission == PngImageLedgerAdmission.SourceMappingLimit)
        {
            AddCoverage(PngCoverageArea.SourceMappings, PngCoverageReasonCode.SourceMappingLimit);
            AddSkip(page, reference, PngDiscoverySkipReason.SourceMappingLimit);
        }
    }

    private void DiscoverPages(
        CrawlUrl document,
        int depth,
        HtmlDocumentDiscovery discovery)
    {
        var resolutionBase = discovery.BaseHref is { } baseHref
            ? CrawlUrlNormalizer.Resolve(baseHref, document, _request.Scope.UrlOptions).Url ?? document
            : document;
        foreach (var href in discovery.NavigationHrefs)
        {
            var resolved = CrawlUrlNormalizer.Resolve(href, resolutionBase, _request.Scope.UrlOptions).Url;
            if (resolved is null
                || _frontier.Scope.Decide(resolved) != CrawlScopeDecision.Internal)
            {
                continue;
            }
            if (depth >= _request.Profile.Pages.MaxDepth)
            {
                AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.DepthLimit);
                continue;
            }

            var admission = _frontier.Offer(resolved, depth + 1);
            TrackPageAdmission(admission);
        }
    }

    private void TrackPageAdmission(CrawlAdmission admission)
    {
        if (admission.SkipReason == CrawlSkipReasons.PageLimit)
        {
            AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.PageLimit);
        }
        else if (admission.SkipReason == CrawlSkipReasons.QueryVariantCap)
        {
            AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.QueryVariantLimit);
        }
    }

    private async Task<bool> IsRobotsAllowedAsync(
        CrawlUrl page,
        CancellationToken cancellationToken)
    {
        if (!_robotsByOrigin.TryGetValue(page.Origin, out var facts))
        {
            facts = await _robotsReader.GetAsync(page.Origin, cancellationToken);
            _robotsByOrigin.Add(page.Origin, facts);
        }

        return CrawlRobotsGate.IsAllowed(facts, _userAgent, page.Path, overrideGranted: false);
    }

    private async Task<SafeHttpRequestHopDecision> BeforeRedirectAsync(
        SafeHttpRequestHop hop,
        CancellationToken cancellationToken)
    {
        if (hop.RedirectCount == 0) return new(true);
        var destination = CrawlUrlNormalizer.Normalize(hop.Url, _request.Scope.UrlOptions).Url;
        if (destination is null
            || _frontier.Scope.Decide(destination) != CrawlScopeDecision.Internal)
        {
            AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.RedirectOutOfScope);
            return new(false, PngCoverageReasonCode.RedirectOutOfScope.ToString());
        }
        if (await IsRobotsAllowedAsync(destination, cancellationToken)) return new(true);

        AddCoverage(PngCoverageArea.Crawl, PngCoverageReasonCode.RobotsDisallowed);
        return new(false, PngCoverageReasonCode.RobotsDisallowed.ToString());
    }

    private void AddSkip(
        PngDiscoveredPage page,
        HtmlImageReference reference,
        PngDiscoverySkipReason reason) =>
        _skips.Add(new(
            page.DisplayUrl,
            page.IdentityHash,
            reference.AttributeKind,
            PngAssetUrlResolver.BoundedDescriptor(reference.Descriptor) is { Length: > 0 } descriptor
                ? descriptor
                : null,
            PngAssetUrlResolver.SafeRawValue(reference.RawUrl, _request.Scope.UrlOptions),
            reason));

    private void AddCoverage(
        PngCoverageArea area,
        PngCoverageReasonCode reason,
        int count = 1)
    {
        var key = (area, reason);
        _coverage[key] = checked(_coverage.GetValueOrDefault(key) + count);
    }

    private void AddCoverageIfMissing(
        PngCoverageArea area,
        PngCoverageReasonCode reason)
    {
        var key = (area, reason);
        if (!_coverage.ContainsKey(key)) _coverage.Add(key, 1);
    }

    private sealed class PageRedirectHopPolicy(PngSiteDiscoveryExecution execution)
        : ISafeHttpRequestHopPolicy
    {
        public Task<SafeHttpRequestHopDecision> EvaluateAsync(
            SafeHttpRequestHop hop,
            CancellationToken cancellationToken = default) =>
            execution.BeforeRedirectAsync(hop, cancellationToken);
    }
}
