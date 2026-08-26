using System.Data.Common;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Crawling;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.Infrastructure.Crawling;

internal sealed record CrawlDependencies(
    ISafeHttpTransport Transport,
    IHtmlLinkExtractor LinkExtractor,
    ICrawlRobotsReader RobotsReader,
    ICrawlResultSink Sink,
    CrawlRequestBudget RequestBudget,
    HostRequestRateLimiter RateLimiter,
    ILogger Logger);

internal sealed class CrawlExecutionService(
    ISafeHttpTransport transport,
    IHtmlLinkExtractor linkExtractor,
    ICrawlRobotsReader robotsReader,
    ICrawlResultSink sink,
    CrawlRequestBudget requestBudget,
    HostRequestRateLimiter rateLimiter,
    CrawlSchedulingOptions options,
    SafeHttpTransportOptions transportOptions,
    TimeProvider timeProvider,
    ILogger<CrawlExecutionService> logger) : ICrawlExecutionService
{
    public async Task<CrawlRunOutcome?> ExecuteAsync(
        CrawlRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await sink.BeginRunAsync(
            new(request.RunId, request.EndpointId, request.SeedUrls ?? [],
                CrawlRunSettings.From(request), timeProvider.GetUtcNow()),
            cancellationToken);

        var executionClaimId = Guid.NewGuid();
        if (!await sink.TryClaimRunAsync(request.RunId, executionClaimId, cancellationToken))
        {
            logger.LogWarning(
                "Crawl run was already claimed or no longer in flight, so this delivery performed "
                + "nothing. CrawlRunId={CrawlRunId} EndpointId={EndpointId}",
                request.RunId, request.EndpointId);
            return null;
        }

        var scope = BuildScope(request, out var seedErrors);
        var errors = seedErrors.Concat(scope?.Validate() ?? []).ToArray();
        if (errors.Length > 0)
        {
            var invalid = CrawlRunOutcome.Invalid(request.RunId, errors);
            await sink.RecordRunOutcomeAsync(invalid, executionClaimId, cancellationToken);
            return invalid;
        }

        var run = new CrawlRunExecution(
            request, scope!, options, transportOptions.UserAgent, timeProvider, executionClaimId,
            new(transport, linkExtractor, robotsReader, sink, requestBudget,
                rateLimiter, logger));
        return await run.ExecuteAsync(cancellationToken);
    }

    private static CrawlScope? BuildScope(CrawlRunRequest request, out IReadOnlyList<string> errors)
    {
        var failures = new List<string>();
        errors = failures;
        var seeds = new List<CrawlUrl>();
        foreach (var seedUrl in request.SeedUrls ?? [])
        {
            var normalized = CrawlUrlNormalizer.Normalize(seedUrl, request.UrlOptions);
            if (normalized.Url is null)
            {
                failures.Add($"The seed '{seedUrl}' is not a crawl target ({normalized.Rejection}).");
                continue;
            }

            seeds.Add(normalized.Url);
        }

        if (seeds.Count == 0)
        {
            failures.Add("A crawl needs at least one usable seed URL.");
            return null;
        }

        var derived = CrawlScope.FromSeeds(seeds);
        return derived with
        {
            AllowedHosts = request.AllowedHosts ?? derived.AllowedHosts,
            AllowedPathPrefixes = request.AllowedPathPrefixes ?? derived.AllowedPathPrefixes
        };
    }
}

internal sealed class CrawlRunExecution
{
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(20);

    private readonly CrawlRunRequest _request;
    private readonly CrawlSchedulingOptions _options;
    private readonly CrawlDependencies _dependencies;
    private readonly TimeProvider _timeProvider;
    private readonly CrawlFrontier _frontier;
    private readonly CrawlLinkLedger _ledger = new();
    private readonly Dictionary<string, CrawlRobotsFacts> _robotsByOrigin = new(StringComparer.Ordinal);
    private readonly HashSet<string> _overriddenOrigins = new(StringComparer.Ordinal);
    private readonly DateTimeOffset _deadline;
    private readonly string _userAgent;
    private readonly CrawlRequestExecutor _requestExecutor;

    private readonly Guid _executionClaimId;

    private readonly Lock _lock = new();

    private readonly SemaphoreSlim _dataAccess = new(1, 1);

    private readonly List<CrawlLinkRecord> _readyRecords = [];
    private readonly Dictionary<string, int> _depthByUrl = new(StringComparer.Ordinal);
    private readonly HashSet<string> _internalUrls = new(StringComparer.Ordinal);

    private int _activeWorkers;
    private int _pagesFetched;
    private int _linksRecorded;
    private int _originsOverridden;
    private string? _firstOverrideRefusal;
    private volatile string? _budgetStopReason;

    private volatile bool _coverageLimited;

    public CrawlRunExecution(
        CrawlRunRequest request,
        CrawlScope scope,
        CrawlSchedulingOptions options,
        string userAgent,
        TimeProvider timeProvider,
        Guid executionClaimId,
        CrawlDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _request = request;
        _options = options;
        _dependencies = dependencies;
        _timeProvider = timeProvider;
        _executionClaimId = executionClaimId;
        _frontier = new(scope, request.Limits);
        _deadline = timeProvider.GetUtcNow() + options.MaxDuration;
        _userAgent = userAgent;
        _requestExecutor = new(request, options, dependencies, timeProvider);

        foreach (var seed in scope.Seeds)
        {
            Track(seed, 0, isInternal: true);
            Buffer(_ledger.RecordDiscovery(null, seed.Value));
        }
    }

    public async Task<CrawlRunOutcome> ExecuteAsync(CancellationToken cancellationToken)
    {
        var cancelled = false;
        var durationExceeded = false;
        Exception? failure = null;

        using var deadlineSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineSource.CancelAfter(_options.MaxDuration);

        try
        {
            await PrepareRobotsAsync(deadlineSource.Token);
            await Task.WhenAll(Enumerable
                .Range(0, Math.Max(1, _options.RequestConcurrency))
                .Select(_ => WorkAsync(deadlineSource.Token)));
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) cancelled = true;
            else durationExceeded = true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = exception;
        }

        try
        {
            lock (_lock) Buffer(_ledger.Flush());
            await DrainAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure ??= exception;
        }

        if (failure is not null)
        {
            _dependencies.Logger.LogError(
                failure,
                "Crawl run {RunId} for endpoint {EndpointId} failed after {PagesFetched} page(s).",
                _request.RunId, _request.EndpointId, _pagesFetched);
        }

        var outcome = Summarize(
            cancelled || cancellationToken.IsCancellationRequested, failure, durationExceeded);

        if (!await _dependencies.Sink.RecordRunOutcomeAsync(
            outcome, _executionClaimId, CancellationToken.None))
        {
            _dependencies.Logger.LogWarning(
                "Crawl run outcome was dropped because the run had already been closed by "
                + "reconciliation. CrawlRunId={CrawlRunId} EndpointId={EndpointId}",
                _request.RunId, _request.EndpointId);
        }

        return outcome;
    }

    private async Task PrepareRobotsAsync(CancellationToken cancellationToken)
    {
        foreach (var origin in _frontier.Scope.Seeds
            .Select(seed => seed.Origin).Distinct(StringComparer.Ordinal))
        {
            await RobotsFactsAsync(origin, cancellationToken);
        }
    }

    private CrawlRunOutcome Summarize(bool cancelled, Exception? failure, bool durationExceeded)
    {
        var stopReason = cancelled ? CrawlStopReasons.Cancelled
            : failure is not null ? CrawlStopReasons.Failed
            : durationExceeded ? CrawlStopReasons.DurationLimit
            : _budgetStopReason
                ?? (_timeProvider.GetUtcNow() >= _deadline ? CrawlStopReasons.DurationLimit : null)
                ?? CrawlStopReasons.FrontierExhausted;

        var status = cancelled ? CrawlRunStatuses.Cancelled
            : failure is not null ? CrawlRunStatuses.Failed
            : CrawlRunStatuses.Completed;

        var granted = _originsOverridden > 0;
        return new(
            _request.RunId,
            status,
            stopReason,
            _pagesFetched,
            _linksRecorded,
            granted,
            granted ? null : _firstOverrideRefusal ?? CrawlOverrideRefusals.NotRequested,
            [])
        {
            FailureCode = failure is null ? null : Classify(failure),
            CoverageLimited = _coverageLimited
        };
    }

    private static string Classify(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case DbException or DbUpdateException:
                    return CrawlFailureCodes.StorageUnavailable;
                case SocketException or AuthenticationException or HttpRequestException or IOException:
                    return CrawlFailureCodes.SiteUnreachable;
            }
        }

        return CrawlFailureCodes.Unexpected;
    }

    private async Task WorkAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_timeProvider.GetUtcNow() >= _deadline)
            {
                _budgetStopReason = CrawlStopReasons.DurationLimit;
                return;
            }

            CrawlWorkItem? item;
            lock (_lock)
            {
                if (_frontier.TryDequeue(out var dequeued))
                {
                    item = dequeued;
                    _activeWorkers++;
                }
                else
                {
                    if (_activeWorkers == 0) return;
                    item = null;
                }
            }

            if (item is null)
            {
                await Task.Delay(IdlePollInterval, _timeProvider, cancellationToken);
                continue;
            }

            try
            {
                await VisitAsync(item, cancellationToken);
            }
            finally
            {
                lock (_lock) _activeWorkers--;
            }

            await DrainAsync(cancellationToken);
        }
    }

    private async Task VisitAsync(CrawlWorkItem item, CancellationToken cancellationToken)
    {
        if (await SkipReasonForAsync(item, cancellationToken) is { } skipReason)
        {
            if (CrawlSkipReasons.LimitsCoverage.Contains(skipReason)) _coverageLimited = true;
            lock (_lock) Buffer(_ledger.RecordSkip(item.Url.Value, skipReason));
            return;
        }

        var result = await FetchAsync(item, cancellationToken);

        lock (_lock)
        {
            Buffer(_ledger.RecordOutcome(
                item.Url.Value,
                Observe(result),
                result.FinalDestination?.Url,
                (int)Math.Clamp(result.Duration.TotalMilliseconds, 0, int.MaxValue)));
        }

        if (item.Mode != CrawlVisitMode.Follow) return;

        if (!ShouldFollow(result))
        {
            if (result.BodyTruncated) _coverageLimited = true;
            return;
        }

        Interlocked.Increment(ref _pagesFetched);

        var document = DocumentToFollow(item, result);
        if (document is null)
        {
            _coverageLimited = true;
            return;
        }

        var links = _dependencies.LinkExtractor.ExtractHrefs(result.Body, result.ContentType);
        if (!links.FullyInspected) _coverageLimited = true;
        var resolutionBase = links.BaseHref is { } baseHref
            ? CrawlUrlNormalizer.Resolve(baseHref, document, _request.UrlOptions).Url ?? document
            : document;
        FollowLinks(document, resolutionBase, item.Depth, links.Hrefs);
    }

    private async Task<SafeHttpTransportResult> FetchAsync(
        CrawlWorkItem item,
        CancellationToken cancellationToken) =>
        await _requestExecutor.ExecuteAsync(
            item.Url.Value,
            item.Url.Host,
            new CrawlRedirectHopPolicy(this),
            cancellationToken);

    private CrawlUrl? DocumentToFollow(
        CrawlWorkItem item,
        SafeHttpTransportResult result)
    {
        var finalUrl = result.FinalDestination?.Url;
        if (finalUrl is null || string.Equals(finalUrl, item.Url.Value, StringComparison.Ordinal))
        {
            return item.Url;
        }

        if (CrawlUrlNormalizer.Normalize(finalUrl, _request.UrlOptions).Url is not { } destination)
        {
            return null;
        }

        if (_frontier.Scope.Decide(destination) != CrawlScopeDecision.Internal) return null;

        return destination;
    }

    private async Task<SafeHttpRequestHopDecision> BeforeRequestAsync(
        SafeHttpRequestHop hop,
        CancellationToken cancellationToken)
    {
        if (hop.RedirectCount == 0) return new(true);
        if (CrawlUrlNormalizer.Normalize(hop.Url, _request.UrlOptions).Url is not { } destination)
        {
            return new(false, CrawlUrlRejections.Malformed);
        }

        var isInternal = _frontier.Scope.Decide(destination) == CrawlScopeDecision.Internal;
        if (!isInternal && !_request.CheckExternalLinks)
        {
            return new(false, CrawlSkipReasons.ExternalCheckDisabled);
        }
        if (isInternal)
        {
            var facts = await RobotsFactsAsync(destination.Origin, cancellationToken);
            if (!CrawlRobotsGate.IsAllowed(
                facts, _userAgent, destination.Path, OverrideFor(destination.Origin)))
            {
                _coverageLimited = true;
                return new(false, CrawlSkipReasons.RobotsDisallowed);
            }
        }

        await _dependencies.RateLimiter.WaitAsync(destination.Host, cancellationToken);
        return new(true);
    }

    private sealed class CrawlRedirectHopPolicy(
        CrawlRunExecution execution) : ISafeHttpRequestHopPolicy
    {
        public Task<SafeHttpRequestHopDecision> EvaluateAsync(
            SafeHttpRequestHop hop,
            CancellationToken cancellationToken = default) =>
            execution.BeforeRequestAsync(hop, cancellationToken);
    }

    private static bool ShouldFollow(SafeHttpTransportResult result) =>
        result.Succeeded && result.StatusCode is >= 200 and <= 299 && !result.BodyTruncated;

    private async Task<string?> SkipReasonForAsync(CrawlWorkItem item, CancellationToken cancellationToken)
    {
        var isInternal = _frontier.Scope.Decide(item.Url) == CrawlScopeDecision.Internal;
        if (!isInternal && !_request.CheckExternalLinks) return CrawlSkipReasons.ExternalCheckDisabled;

        if (!isInternal) return null;

        var facts = await RobotsFactsAsync(item.Url.Origin, cancellationToken);
        var granted = OverrideFor(item.Url.Origin);
        return CrawlRobotsGate.IsAllowed(facts, _userAgent, item.Url.Path, granted)
            ? null
            : CrawlSkipReasons.RobotsDisallowed;
    }

    private bool OverrideFor(string origin)
    {
        var decision = CrawlRobotsGate.EvaluateOverride(_request.RequestRobotsOverride);

        lock (_lock)
        {
            if (decision.Granted)
            {
                if (_overriddenOrigins.Add(origin)) _originsOverridden++;
            }
            else
            {
                _firstOverrideRefusal ??= decision.RefusedBecause;
            }
        }

        return decision.Granted;
    }

    private async Task<CrawlRobotsFacts> RobotsFactsAsync(string origin, CancellationToken cancellationToken)
    {
        await _dataAccess.WaitAsync(cancellationToken);
        try
        {
            if (_robotsByOrigin.TryGetValue(origin, out var cached)) return cached;

            var facts = await _dependencies.RobotsReader.GetAsync(origin, cancellationToken);
            _robotsByOrigin[origin] = facts;
            return facts;
        }
        finally
        {
            _dataAccess.Release();
        }
    }

    private void FollowLinks(
        CrawlUrl document,
        CrawlUrl resolutionBase,
        int depth,
        IReadOnlyList<string> hrefs)
    {
        foreach (var href in hrefs)
        {
            var resolved = CrawlUrlNormalizer.Resolve(href, resolutionBase, _request.UrlOptions);
            if (resolved.Url is null)
            {
                RecordRejectedHref(document, href, resolved.Rejection);
                continue;
            }

            lock (_lock)
            {
                Track(resolved.Url, depth + 1,
                    _frontier.Scope.Decide(resolved.Url) == CrawlScopeDecision.Internal);

                var admission = _frontier.Offer(resolved.Url, depth + 1);
                Buffer(_ledger.RecordDiscovery(document.Value, resolved.Url.Value));

                if (admission is { Admitted: false, SkipReason: not (null or CrawlSkipReasons.AlreadySeen) })
                {
                    if (admission.SkipReason == CrawlSkipReasons.PageLimit)
                    {
                        _budgetStopReason = CrawlStopReasons.PageLimit;
                    }

                    if (CrawlSkipReasons.LimitsCoverage.Contains(admission.SkipReason!))
                    {
                        _coverageLimited = true;
                    }

                    Buffer(_ledger.RecordSkip(resolved.Url.Value, admission.SkipReason));
                }
            }
        }
    }

    private void RecordRejectedHref(CrawlUrl document, string href, string? rejection)
    {
        if (rejection is null or CrawlUrlRejections.UnsupportedScheme) return;

        var authored = href.Trim();
        if (authored.Length == 0) return;
        if (authored.Length > CrawlUrlOptions.MaxUrlLength)
        {
            authored = authored[..CrawlUrlOptions.MaxUrlLength];
        }

        lock (_lock)
        {
            Buffer(_ledger.RecordDiscovery(document.Value, authored));
            Buffer(_ledger.RecordSkip(authored, rejection));
        }
    }

    private static CrawlRequestObservation Observe(SafeHttpTransportResult result) => new(
        result.Failure switch
        {
            null => CrawlRequestOutcome.Responded,
            SafeHttpFailureKind.Timeout or SafeHttpFailureKind.Cancelled => CrawlRequestOutcome.Timeout,
            SafeHttpFailureKind.DestinationRejected or SafeHttpFailureKind.RequestPolicyRejected
                => CrawlRequestOutcome.Blocked,
            SafeHttpFailureKind.RedirectLoop or SafeHttpFailureKind.RedirectLimit
                or SafeHttpFailureKind.RedirectInvalid or SafeHttpFailureKind.RedirectMissingLocation
                => CrawlRequestOutcome.Broken,
            _ => CrawlRequestOutcome.Failed
        },
        result.StatusCode,
        result.Redirects.Count);

    private void Track(CrawlUrl url, int depth, bool isInternal)
    {
        _depthByUrl.TryAdd(url.Value, depth);
        if (isInternal) _internalUrls.Add(url.Value);
    }

    private void Buffer(IReadOnlyList<CrawlEdge> edges)
    {
        foreach (var edge in edges)
        {
            _readyRecords.Add(new(
                _request.RunId,
                CrawlUrlRedactor.Redact(edge.SourceUrl, _request.UrlOptions),
                CrawlUrlRedactor.Redact(edge.TargetUrl, _request.UrlOptions)!,
                _internalUrls.Contains(edge.TargetUrl),
                _depthByUrl.GetValueOrDefault(edge.TargetUrl, -1),
                edge.Classification,
                edge.StatusCode,
                edge.RedirectCount,
                CrawlUrlRedactor.Redact(edge.FinalUrl, _request.UrlOptions),
                edge.SkipReason,
                edge.DurationMs)
            {
                SourceUrlIdentity = edge.SourceUrl,
                TargetUrlIdentity = edge.TargetUrl
            });
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        CrawlLinkRecord[] pending;
        lock (_lock)
        {
            if (_readyRecords.Count == 0) return;
            pending = [.. _readyRecords];
            _readyRecords.Clear();
        }

        await _dataAccess.WaitAsync(CancellationToken.None);
        try
        {
            var persisted = await _dependencies.Sink.RecordLinksAsync(pending, cancellationToken);
            Interlocked.Exchange(ref _linksRecorded, persisted);
        }
        catch
        {
            lock (_lock) _readyRecords.InsertRange(0, pending);
            throw;
        }
        finally
        {
            _dataAccess.Release();
        }
    }
}
