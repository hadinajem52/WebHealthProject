using Microsoft.Extensions.Logging;
using WebHealth.Application.Crawling;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>The collaborators one run drives, grouped so the run's own signature stays readable.</summary>
internal sealed record CrawlDependencies(
    ISafeHttpTransport Transport,
    IHtmlLinkExtractor LinkExtractor,
    ICrawlRobotsReader RobotsReader,
    ICrawlResultSink Sink,
    IMonitoringTargetAuthorizer TargetAuthorizer,
    CrawlRequestBudget RequestBudget,
    HostRequestRateLimiter RateLimiter,
    ILogger Logger);

/// <summary>
/// BR-L01 to BR-L10. Drives the frontier from 6.5 through the same <see cref="ISafeHttpTransport" />
/// every other outbound request uses, so the actual-connection SSRF control, the destination policy,
/// the target-authorization evidence and the bounded body are inherited rather than reimplemented.
/// There is no second HTTP client and no bypass.
/// <para>
/// Results are handed to the sink as each target resolves. That is what makes BR-L10 need no special
/// cancellation path: whatever the run found is already recorded when it stops.
/// </para>
/// </summary>
internal sealed class CrawlExecutionService(
    ISafeHttpTransport transport,
    IHtmlLinkExtractor linkExtractor,
    ICrawlRobotsReader robotsReader,
    ICrawlResultSink sink,
    IMonitoringTargetAuthorizer targetAuthorizer,
    CrawlRequestBudget requestBudget,
    HostRequestRateLimiter rateLimiter,
    CrawlSchedulingOptions options,
    SafeHttpTransportOptions transportOptions,
    TimeProvider timeProvider,
    ILogger<CrawlExecutionService> logger) : ICrawlExecutionService
{
    public async Task<CrawlRunOutcome> ExecuteAsync(
        CrawlRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The run is opened before validation, not after. Someone asked for this crawl, and a
        // request that was refused is a fact worth keeping — recording the run only on the success
        // path would leave a misconfigured crawl indistinguishable from one nobody ever started.
        await sink.BeginRunAsync(
            new(request.RunId, request.EndpointId, request.SeedUrls ?? [],
                CrawlRunSettings.From(request), timeProvider.GetUtcNow()),
            cancellationToken);

        var scope = BuildScope(request, out var seedErrors);
        var errors = seedErrors.Concat(scope?.Validate() ?? []).ToArray();
        if (errors.Length > 0)
        {
            var invalid = CrawlRunOutcome.Invalid(request.RunId, errors);
            await sink.RecordRunOutcomeAsync(invalid, cancellationToken);
            return invalid;
        }

        var run = new CrawlRunExecution(
            request, scope!, options, transportOptions.UserAgent, timeProvider,
            new(transport, linkExtractor, robotsReader, sink, targetAuthorizer, requestBudget,
                rateLimiter, logger));
        return await run.ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds are canonicalised before anything else, because a seed that is not a crawl target is a
    /// configuration error and must surface as one rather than as an empty run.
    /// </summary>
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

/// <summary>
/// One run's mutable state while it executes, separate from the service so the service stays a
/// stateless scoped dependency: two runs share no frontier and no ledger. The request budget and
/// the per-host rate limiter are deliberately **not** per run — they are process-wide, because a
/// limit each run respects on its own does not bound what several runs do together.
/// Distinct from the <c>CrawlRun</c> entity, which is what 6.7 stores once this has finished.
/// </summary>
internal sealed class CrawlRunExecution
{
    /// <summary>How long an idle worker waits before re-checking a frontier its peers are filling.</summary>
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

    /// <summary>Guards the frontier, the ledger and the bookkeeping they are advanced with.</summary>
    private readonly Lock _lock = new();

    /// <summary>
    /// Serialises everything that reaches the database. The robots reader, the target authorizer and
    /// the sink all sit on one scoped <c>DbContext</c>, which is not thread-safe — and this loop is
    /// the first place in the project that runs several requests at once. Its cost is network, not
    /// query time, so serialising the queries costs nothing worth measuring.
    /// </summary>
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

    /// <summary>
    /// Set the moment anything leaves part of the site unexamined. It is what separates a frontier
    /// that drained because the site was covered from one that drained because the pages nobody
    /// could read had no links to offer.
    /// </summary>
    private volatile bool _coverageLimited;

    public CrawlRunExecution(
        CrawlRunRequest request,
        CrawlScope scope,
        CrawlSchedulingOptions options,
        string userAgent,
        TimeProvider timeProvider,
        CrawlDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _request = request;
        _options = options;
        _dependencies = dependencies;
        _timeProvider = timeProvider;
        _frontier = new(scope, request.Limits);
        _deadline = timeProvider.GetUtcNow() + options.MaxDuration;
        _userAgent = userAgent;

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

        // The deadline is a cancellation token, not only a between-items check. A worker parked in
        // the rate limiter or waiting on a slow host would otherwise run past the run's duration
        // limit with nothing able to interrupt it (BR-L05).
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
            // A run stopped by its own deadline has not been cancelled by anyone: it hit a budget,
            // which is a completed run with a stop reason rather than an abandoned one.
            if (cancellationToken.IsCancellationRequested) cancelled = true;
            else durationExceeded = true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // BR-L05 asks a crawl to stop gracefully. A run that threw its way out would lose the
            // outcome record along with every result it had already found, and would look to the
            // reader exactly like a run that was never started.
            failure = exception;
        }

        // Flushing and recording the outcome happen on every path, and never under the run's own
        // cancellation token: the work of preserving what was found must not itself be cancellable.
        try
        {
            lock (_lock) Buffer(_ledger.Flush());
            await DrainAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Only if nothing had failed already: the first exception is the one that explains the
            // run, and a flush that then failed is usually its consequence.
            failure ??= exception;
        }

        if (failure is not null)
        {
            // Logged as well as stored. The stored reason is bounded and written for a reader of
            // the report; the log keeps the stack trace, which is what a defect is diagnosed from.
            _dependencies.Logger.LogError(
                failure,
                "Crawl run {RunId} for endpoint {EndpointId} failed after {PagesFetched} page(s).",
                _request.RunId, _request.EndpointId, _pagesFetched);
        }

        var outcome = Summarize(
            cancelled || cancellationToken.IsCancellationRequested, failure, durationExceeded);
        await _dependencies.Sink.RecordRunOutcomeAsync(outcome, CancellationToken.None);
        return outcome;
    }

    /// <summary>
    /// Loads the seed origins' robots facts up front, so the first request cannot be made before the
    /// policy governing it has been read. The override decision itself is **not** settled here: it
    /// is made per origin, at the point each origin is first consulted.
    /// </summary>
    private async Task PrepareRobotsAsync(CancellationToken cancellationToken)
    {
        foreach (var origin in _frontier.Scope.Seeds
            .Select(seed => seed.Origin).Distinct(StringComparer.Ordinal))
        {
            await RobotsFactsAsync(origin, cancellationToken);
        }
    }

    /// <summary>
    /// Cancellation is reported ahead of failure, and failure ahead of any budget: they are the
    /// reasons the run stopped early, and a budget that also happened to bind says less about what
    /// the reader is looking at.
    /// </summary>
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

        // BR-L02 reporting: the flag answers "did this run bypass a published restriction anywhere?",
        // which is the security-relevant fact. Where an origin's override was refused its robots
        // were enforced, so a refusal is not a failure of the run — but it must still be visible.
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
            FailureDetail = failure is null ? null : Describe(failure),
            CoverageLimited = _coverageLimited
        };
    }

    /// <summary>
    /// The exception chain as one sentence a reader of the report can act on. The inner exceptions
    /// are what carry the actual cause -- an <c>HttpRequestException</c> alone says "a request
    /// failed" where its inner socket error says which host refused and why -- so the chain is
    /// kept rather than only its outermost frame.
    /// </summary>
    private static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null && parts.Count < 3; current = current.InnerException)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" -> ", parts);
    }

    /// <summary>
    /// A worker exits only when the frontier is empty **and** no peer is still fetching. Exiting on
    /// an empty frontier alone would end every worker but one immediately after the seeds are taken,
    /// leaving the configured concurrency unused for the rest of the run.
    /// </summary>
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
                // Dequeueing and the "is anyone still working?" test share the lock deliberately.
                // Read apart, a worker could see an empty frontier, then see the active count reach
                // zero after a peer had already enqueued its discoveries, and exit leaving queued
                // work behind. Under one lock, an empty frontier with no active worker means no
                // further item can ever arrive.
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

        await _dependencies.RateLimiter.WaitAsync(item.Url.Host, cancellationToken);

        // The crawler's share of the shared transport budget, held for exactly as long as the
        // request. Acquired here rather than around the whole visit so parsing and bookkeeping do
        // not hold a slot that monitoring could be using.
        SafeHttpTransportResult result;
        using (await _dependencies.RequestBudget.AcquireAsync(cancellationToken))
        {
            result = await _dependencies.Transport.SendAsync(
                new(_request.EndpointId, item.Url.Value, _request.IsProduction,
                    MaxResponseBodyBytes: _options.MaxPageBytes,
                    TimeoutSeconds: _options.FetchTimeoutSeconds),
                cancellationToken);
        }

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
            // A page whose body was cut short is fetched and never read. ShouldFollow refuses it
            // because a truncated document's last href may be cut mid-URL, which is right, and it
            // still means this page's links are missing rather than absent.
            if (result.BodyTruncated) _coverageLimited = true;
            return;
        }

        Interlocked.Increment(ref _pagesFetched);

        // The transport follows redirects itself, so the document in hand is not necessarily the
        // one this work item names. Where it came from decides everything below: whether its links
        // are ours to inspect, and what every relative href resolves against.
        var document = await DocumentToFollowAsync(item, result, cancellationToken);
        if (document is null)
        {
            _coverageLimited = true;
            return;
        }

        // The body is turned into a list of hrefs and then dropped. Nothing downstream of this line
        // can see the document (BR-E10).
        var links = _dependencies.LinkExtractor.ExtractHrefs(result.Body, result.ContentType);
        if (!links.FullyInspected) _coverageLimited = true;
        FollowLinks(document, item.Depth, links.Hrefs);
    }

    /// <summary>
    /// The document a response actually is, or null when its links must not be followed.
    /// <para>
    /// A redirect chain is resolved inside the transport, which re-checks target authorization at
    /// every hop but knows nothing of this crawl's scope, robots policy or per-host pacing. Those
    /// were decided for the URL that was asked for, and a response from somewhere else has not
    /// been through them.
    /// </para>
    /// </summary>
    private async Task<CrawlUrl?> DocumentToFollowAsync(
        CrawlWorkItem item,
        SafeHttpTransportResult result,
        CancellationToken cancellationToken)
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

        // An internal URL that redirects off-site hands back a document this crawl has no business
        // reading. Parsing it would attribute another site's links to a page of ours, and expand
        // the crawl into a site nobody put in its scope.
        if (_frontier.Scope.Decide(destination) != CrawlScopeDecision.Internal) return null;

        // The chain spent a request on this host that the crawler's own limiter never counted, so
        // it is charged here. Spacing the next request is the only correction still available once
        // the response is in hand.
        if (!string.Equals(destination.Host, item.Url.Host, StringComparison.OrdinalIgnoreCase))
        {
            await _dependencies.RateLimiter.WaitAsync(destination.Host, cancellationToken);
        }

        var facts = await RobotsFactsAsync(destination.Origin, cancellationToken);
        return CrawlRobotsGate.IsAllowed(
            facts, _userAgent, destination.Path, OverrideFor(destination.Origin, facts))
            ? destination
            : null;
    }

    /// <summary>
    /// Only a successful response is a page. A 404 or 500 that happens to carry an HTML error page
    /// is not one: counting it would fill the page budget with error pages, and following its
    /// navigation would expand the crawl out of a document the site does not consider a page.
    /// <para>
    /// A truncated body is not followed either. The parser will still read it, but its final
    /// <c>href</c> may have been cut mid-URL, and a cut-off URL resolves to a target the page never
    /// contained — which would then be reported as a broken link that does not exist.
    /// </para>
    /// </summary>
    private static bool ShouldFollow(SafeHttpTransportResult result) =>
        result.Succeeded && result.StatusCode is >= 200 and <= 299 && !result.BodyTruncated;

    /// <summary>
    /// Why this URL will not be requested, or null to request it. Robots and authorization are both
    /// decided here, so a URL cannot reach the transport without having passed them.
    /// </summary>
    private async Task<string?> SkipReasonForAsync(CrawlWorkItem item, CancellationToken cancellationToken)
    {
        var isInternal = _frontier.Scope.Decide(item.Url) == CrawlScopeDecision.Internal;
        if (!isInternal && !_request.CheckExternalLinks) return CrawlSkipReasons.ExternalCheckDisabled;

        // The transport enforces authorization too. Checking first means an unauthorized host is
        // recorded with the reason it was skipped, rather than as a target we tried and were refused
        // — and it means we never open a connection to a host nothing entitles us to reach.
        await _dataAccess.WaitAsync(cancellationToken);
        try
        {
            if (!await _dependencies.TargetAuthorizer.IsAuthorizedAsync(
                _request.EndpointId, item.Url.Host, item.Url.Port,
                _timeProvider.GetUtcNow(), cancellationToken))
            {
                return CrawlSkipReasons.TargetNotAuthorized;
            }
        }
        finally
        {
            _dataAccess.Release();
        }

        if (!isInternal) return null;

        var facts = await RobotsFactsAsync(item.Url.Origin, cancellationToken);
        var granted = OverrideFor(item.Url.Origin, facts);
        return CrawlRobotsGate.IsAllowed(facts, _userAgent, item.Url.Path, granted)
            ? null
            : CrawlSkipReasons.RobotsDisallowed;
    }

    /// <summary>
    /// BR-L02, decided **per origin**. An approved exception authorizes bypassing that origin's
    /// published restrictions and no other: a run whose scope reaches a second host would otherwise
    /// carry the seed's approval onto a host nobody approved, which is the whole thing the approval
    /// exists to prevent.
    /// </summary>
    private bool OverrideFor(string origin, CrawlRobotsFacts facts)
    {
        var decision = CrawlRobotsGate.EvaluateOverride(
            _request.RequestRobotsOverride, _request.IsProduction, facts);

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

    /// <summary>
    /// One read per origin for the life of the run. A crawl of a thousand pages on one host must
    /// produce one robots lookup, for the same reason 6.4 fetches robots.txt once per origin.
    /// </summary>
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

    /// <summary>
    /// <paramref name="document" /> is where the response came from, which after a redirect is not
    /// the URL that was requested. It is both the base every relative href resolves against and the
    /// page the discovered links are recorded under, because it is the document that contains them.
    /// </summary>
    private void FollowLinks(CrawlUrl document, int depth, IReadOnlyList<string> hrefs)
    {
        foreach (var href in hrefs)
        {
            var resolved = CrawlUrlNormalizer.Resolve(href, document, _request.UrlOptions);
            if (resolved.Url is null)
            {
                RecordRejectedHref(document, href, resolved.Rejection);
                continue;
            }

            lock (_lock)
            {
                // Scope and depth are tracked whether or not the frontier admits the URL. Recording
                // them only on admission made a page-limited internal link report as external at
                // depth -1, which is a lie about a link the report exists to explain.
                Track(resolved.Url, depth + 1,
                    _frontier.Scope.Decide(resolved.Url) == CrawlScopeDecision.Internal);

                var admission = _frontier.Offer(resolved.Url, depth + 1);
                Buffer(_ledger.RecordDiscovery(document.Value, resolved.Url.Value));

                // A skip that is not "we already know about this" resolves the target here: the
                // report has to distinguish a link nobody checked from a link that is fine.
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

    /// <summary>
    /// A URL that could not be canonicalised is recorded against the page that authored it, so the
    /// query-parameter cap and a malformed or overlong href are visible rather than silently
    /// dropped.
    /// <para>
    /// <c>UnsupportedScheme</c> is the deliberate exception. <c>mailto:</c>, <c>tel:</c> and
    /// <c>javascript:</c> are ordinary page content rather than defects, and recording each one
    /// would bury the broken links this report exists for under every contact link on the site.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Maps the transport's failure kinds onto the four cases classification depends on. A refusal
    /// by our own policy is <c>Blocked</c>, not <c>Broken</c>: the link may be perfectly good, and
    /// reporting it as broken would blame the site for our configuration.
    /// </summary>
    private static CrawlRequestObservation Observe(SafeHttpTransportResult result) => new(
        result.Failure switch
        {
            null => CrawlRequestOutcome.Responded,
            SafeHttpFailureKind.Timeout or SafeHttpFailureKind.Cancelled => CrawlRequestOutcome.Timeout,
            SafeHttpFailureKind.TargetNotAuthorized or SafeHttpFailureKind.DestinationRejected
                => CrawlRequestOutcome.Blocked,
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
                edge.SourceUrl,
                edge.TargetUrl,
                _internalUrls.Contains(edge.TargetUrl),
                _depthByUrl.GetValueOrDefault(edge.TargetUrl, -1),
                edge.Classification,
                edge.StatusCode,
                edge.RedirectCount,
                edge.FinalUrl,
                edge.SkipReason,
                edge.DurationMs));
        }
    }

    /// <summary>
    /// Hands buffered results to the sink outside the frontier lock. Writing them as they resolve
    /// rather than at the end is the whole of BR-L10's preservation guarantee.
    /// <para>
    /// Records leave the buffer only once the sink has taken them. A sink that fails part way
    /// through — a lost connection, a constraint violation — puts the unwritten remainder back, so
    /// a transient write failure costs a retry rather than the findings themselves.
    /// </para>
    /// </summary>
    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        CrawlLinkRecord[] pending;
        lock (_lock)
        {
            if (_readyRecords.Count == 0) return;
            pending = [.. _readyRecords];
            _readyRecords.Clear();
        }

        var written = 0;
        await _dataAccess.WaitAsync(CancellationToken.None);
        try
        {
            foreach (var record in pending)
            {
                await _dependencies.Sink.RecordLinkAsync(record, cancellationToken);
                written++;
                Interlocked.Increment(ref _linksRecorded);
            }
        }
        finally
        {
            _dataAccess.Release();
            if (written < pending.Length)
            {
                lock (_lock) _readyRecords.InsertRange(0, pending[written..]);
            }
        }
    }
}
