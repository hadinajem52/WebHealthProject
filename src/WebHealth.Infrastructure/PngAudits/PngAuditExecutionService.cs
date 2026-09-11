using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Application.PngAudits;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.Normalization;
using WebHealth.Domain.PngAudits;

namespace WebHealth.Infrastructure.PngAudits;

public sealed class PngAuditExecutionService(
    IPngAuditResultSink sink,
    PngAuditQueuedRunReader runReader,
    IPngSiteCrawler crawler,
    IPngImageTransport imageTransport,
    IPngImageAnalyzer analyzer,
    PngAuditOptions options,
    TimeProvider timeProvider,
    ILogger<PngAuditExecutionService> logger)
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(2);

    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var claim = await sink.TryClaimAsync(runId, cancellationToken);
        if (claim is null) return;

        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var lease = new PngAuditLeaseState();
        var heartbeat = MaintainLeaseAsync(claim, lease, execution);
        try
        {
            await ExecuteClaimAsync(claim, lease, execution.Token);
        }
        catch (PngAuditLeaseLostException)
        {
        }
        catch (OperationCanceledException) when (lease.Lost || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "PNG audit execution failed. PngAuditRunId={PngAuditRunId} ExceptionType={ExceptionType}",
                runId,
                exception.GetType().Name);
            await TryFailAsync(
                claim,
                PngAuditFailureCodes.Unexpected,
                "The PNG audit stopped because of an unexpected execution error.");
        }
        finally
        {
            await execution.CancelAsync();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task ExecuteClaimAsync(
        PngAuditRunClaim claim,
        PngAuditLeaseState lease,
        CancellationToken cancellationToken)
    {
        var target = await runReader.ReadAsync(claim, cancellationToken);
        if (target.State != PngAuditTargetState.Ready)
        {
            await FailTargetAsync(claim, target.State, cancellationToken);
            return;
        }

        var snapshot = claim.Snapshot;
        var runDeadline = claim.StartedAt + snapshot.DiscoveryProfile.Fetch.MaxDuration;
        var scope = new PngSiteDiscoveryScope(
            target.SeedUrl!,
            snapshot.AllowedPageHosts,
            snapshot.AllowedPagePathPrefixes,
            snapshot.AllowedAssetHosts,
            snapshot.UrlOptions);
        var stored = await runReader.ReadProgressAsync(claim.RunId, cancellationToken);
        var lastProgressWrite = DateTimeOffset.MinValue;
        async ValueTask ReportProgressAsync(
            PngSiteDiscoveryProgress reported,
            CancellationToken progressCancellation)
        {
            var now = timeProvider.GetUtcNow();
            if (now - lastProgressWrite < ProgressInterval)
            {
                return;
            }

            lastProgressWrite = now;
            var normalized = new PngAuditCrawlProgress(
                Math.Max(stored.PagesDiscovered, reported.PagesDiscovered),
                Math.Max(stored.HttpAttempts, reported.HttpAttempts),
                Math.Max(stored.TotalPageBytes, reported.TotalPageBytes));
            if (!await sink.UpdateCrawlProgressAsync(
                    claim.RunId,
                    claim.LeaseToken,
                    normalized,
                    progressCancellation))
            {
                throw new PngAuditLeaseLostException();
            }
        }

        var discovery = await crawler.DiscoverAsync(
            new(
                claim.RunId,
                snapshot.EndpointId,
                snapshot.IsProduction,
                scope,
                snapshot.DiscoveryProfile)
            {
                ConsumedHttpAttempts = stored.HttpAttempts,
                ConsumedPageBytes = stored.TotalPageBytes,
                RunDeadline = runDeadline
            },
            cancellationToken,
            ReportProgressAsync);
        ThrowIfLeaseLost(lease);

        var coverage = new PngCoverageTracker(discovery.CoverageReasons);
        var pagesDiscovered = Math.Max(stored.PagesDiscovered, discovery.Pages.Count);
        var discoveredNewImages = discovery.Images
            .Where(image => !stored.ImageIdentityHashes.Contains(image.IdentityHash))
            .ToArray();
        var availableImageSlots = Math.Max(
            0,
            snapshot.DiscoveryProfile.Images.MaxUniqueImages - stored.ImageCount);
        var newImages = discoveredNewImages.Take(availableImageSlots).ToArray();
        if (newImages.Length < discoveredNewImages.Length)
        {
            coverage.Add(
                PngCoverageArea.ImageAnalysis,
                PngCoverageReasonCode.UniqueImageLimit,
                discoveredNewImages.Length - newImages.Length);
        }

        var acceptedImageHashes = stored.ImageIdentityHashes
            .Concat(newImages.Select(image => image.IdentityHash))
            .ToHashSet(StringComparer.Ordinal);
        var candidateMappings = discovery.SourceMappings
            .Where(mapping => acceptedImageHashes.Contains(mapping.ImageIdentityHash)
                && !stored.SourceMappingIdentities.Contains(PngSourceMappingIdentity.Create(
                    mapping.ImageIdentityHash,
                    mapping.SourcePageIdentityHash,
                    mapping.AttributeKind,
                    mapping.Descriptor)))
            .ToArray();
        var availableMappingSlots = Math.Max(
            0,
            snapshot.DiscoveryProfile.Images.MaxTotalSourceMappings - stored.SourceMappingCount);
        var acceptedMappings = candidateMappings.Take(availableMappingSlots).ToArray();
        if (acceptedMappings.Length < candidateMappings.Length)
        {
            coverage.Add(
                PngCoverageArea.SourceMappings,
                PngCoverageReasonCode.SourceMappingLimit,
                candidateMappings.Length - acceptedMappings.Length);
        }

        var mappingsByImage = acceptedMappings
            .GroupBy(mapping => mapping.ImageIdentityHash, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PngImageSourceMapping>)[.. group],
                StringComparer.Ordinal);
        var existingMappings = discovery.Images
            .Where(image => stored.ImageIdentityHashes.Contains(image.IdentityHash))
            .SelectMany(image => MappingsFor(image.IdentityHash, mappingsByImage))
            .ToArray();
        var httpAttempts = discovery.HttpAttempts;
        var totalImageBytes = stored.TotalImageBytes;
        PngAuditCrawlProgress Progress() =>
            new(pagesDiscovered, httpAttempts, discovery.TotalPageBytes);
        await RecordAsync(
            claim,
            new([], existingMappings, discovery.Skips, coverage.Records, Progress()),
            cancellationToken);

        for (var index = 0; index < newImages.Length; index++)
        {
            ThrowIfLeaseLost(lease);
            var remainingDuration = runDeadline - timeProvider.GetUtcNow();
            if (remainingDuration <= TimeSpan.Zero)
            {
                coverage.Add(PngCoverageArea.ImageAnalysis, PngCoverageReasonCode.DurationLimit);
                await RecordUnprocessedAsync(
                    claim,
                    newImages[index..],
                    mappingsByImage,
                    PngCoverageReasonCode.DurationLimit.ToString(),
                    coverage,
                    Progress(),
                    cancellationToken);
                break;
            }
            if (httpAttempts >= snapshot.DiscoveryProfile.Fetch.MaxTotalHttpAttempts)
            {
                coverage.Add(PngCoverageArea.ImageAnalysis, PngCoverageReasonCode.HttpAttemptLimit);
                await RecordUnprocessedAsync(
                    claim,
                    newImages[index..],
                    mappingsByImage,
                    PngCoverageReasonCode.HttpAttemptLimit.ToString(),
                    coverage,
                    Progress(),
                    cancellationToken);
                break;
            }

            var remainingBytes = snapshot.MaxTotalImageBytes - totalImageBytes;
            if (remainingBytes <= 0)
            {
                coverage.Add(PngCoverageArea.ImageAnalysis, PngCoverageReasonCode.TotalImageBytesLimit);
                await RecordUnprocessedAsync(
                    claim,
                    newImages[index..],
                    mappingsByImage,
                    PngCoverageReasonCode.TotalImageBytesLimit.ToString(),
                    coverage,
                    Progress(),
                    cancellationToken);
                break;
            }

            var image = newImages[index];
            using var deadline = new CancellationTokenSource(remainingDuration, timeProvider);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadline.Token);
            var remainingAttempts = snapshot.DiscoveryProfile.Fetch.MaxTotalHttpAttempts
                - httpAttempts;
            var response = await imageTransport.SendAsync(
                new(
                    snapshot.EndpointId,
                    image.FetchUrl,
                    snapshot.IsProduction,
                    Math.Min(SafeHttpTransportDefaults.MaxRedirects, remainingAttempts - 1),
                    checked((int)Math.Min(snapshot.AnalysisLimits.MaxEncodedBytes, remainingBytes)),
                    snapshot.DiscoveryProfile.Fetch.TimeoutSeconds)
                {
                    HopPolicy = new PngAssetRedirectPolicy(snapshot.AllowedAssetHosts),
                    RequestsPerSecondPerHost =
                        snapshot.DiscoveryProfile.Fetch.RequestsPerSecondPerHost,
                    MaxOutboundRequests = remainingAttempts,
                    TransientRetryCount = Math.Max(
                        0,
                        Math.Min(
                            snapshot.DiscoveryProfile.Fetch.TransientRetryCount,
                            remainingAttempts - 1))
                },
                operation.Token);
            httpAttempts = checked(httpAttempts + response.OutboundRequestCount);
            totalImageBytes = checked(totalImageBytes + response.Body.Length);
            ThrowIfLeaseLost(lease);

            PngAuditImageRecord record;
            if (deadline.IsCancellationRequested)
            {
                coverage.Add(PngCoverageArea.ImageAnalysis, PngCoverageReasonCode.DurationLimit);
                record = IncompleteRecord(image, response, snapshot, PngCoverageReasonCode.DurationLimit);
            }
            else
            {
                try
                {
                    record = await CreateImageRecordAsync(
                        image,
                        response,
                        snapshot,
                        operation.Token);
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    coverage.Add(PngCoverageArea.ImageAnalysis, PngCoverageReasonCode.DurationLimit);
                    record = IncompleteRecord(
                        image,
                        response,
                        snapshot,
                        PngCoverageReasonCode.DurationLimit);
                }
            }

            if (response.BodyTruncated && remainingBytes < snapshot.AnalysisLimits.MaxEncodedBytes)
            {
                coverage.Add(
                    PngCoverageArea.ImageAnalysis,
                    PngCoverageReasonCode.TotalImageBytesLimit);
            }
            if (response.Failure == SafeHttpFailureKind.RequestPolicyRejected
                && response.PolicyRejectionReason == PngCoverageReasonCode.RedirectOutOfScope.ToString())
            {
                coverage.Add(
                    PngCoverageArea.ImageAnalysis,
                    PngCoverageReasonCode.RedirectOutOfScope);
            }

            await RecordAsync(
                claim,
                new(
                    [record],
                    MappingsFor(image.IdentityHash, mappingsByImage),
                    [],
                    coverage.Records,
                    Progress()),
                cancellationToken);

            if (deadline.IsCancellationRequested)
            {
                await RecordUnprocessedAsync(
                    claim,
                    newImages[(index + 1)..],
                    mappingsByImage,
                    PngCoverageReasonCode.DurationLimit.ToString(),
                    coverage,
                    Progress(),
                    cancellationToken);
                break;
            }
        }

        ThrowIfLeaseLost(lease);
        var finalProgress = await runReader.ReadProgressAsync(claim.RunId, cancellationToken);
        var crawlLimited = finalProgress.CrawlCoverageLimited
            || discovery.CoverageReasons.Any(reason => reason.Area == PngCoverageArea.Crawl);
        var imageLimited = finalProgress.ImageAnalysisCoverageLimited
            || finalProgress.HasImageProblems;
        var sourceLimited = finalProgress.SourceMappingCoverageLimited
            || discovery.CoverageReasons.Any(reason => reason.Area == PngCoverageArea.SourceMappings);
        var completed = await sink.CompleteAsync(
            claim.RunId,
            claim.LeaseToken,
            new(
                pagesDiscovered,
                finalProgress.ImageCount,
                finalProgress.AnalyzedCount,
                finalProgress.RecommendationCount,
                finalProgress.DiscoverySkipCount,
                httpAttempts,
                discovery.TotalPageBytes,
                finalProgress.TotalImageBytes,
                crawlLimited,
                imageLimited,
                sourceLimited),
            cancellationToken);
        if (!completed) throw new PngAuditLeaseLostException();
    }

    private async Task<PngAuditImageRecord> CreateImageRecordAsync(
        PngDiscoveredImageRequest image,
        SafeHttpTransportResult response,
        PngAuditRunSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var identity = new PngAuditImageIdentity(image.DisplayUrl, image.IdentityHash);
        if (response.Failure is { } failure)
        {
            var reason = response.PolicyRejectionReason is { Length: > 0 } policyReason
                ? policyReason
                : failure.ToString();
            return PngAuditImageRecord.FetchFailed(identity, reason);
        }

        var final = FinalIdentity(image, response, snapshot.UrlOptions);
        if (response.StatusCode is not (>= 200 and <= 299))
        {
            return response.StatusCode is { } status
                ? PngAuditImageRecord.HttpNonSuccess(
                    identity,
                    final.DisplayUrl,
                    final.IdentityHash,
                    response.ContentType,
                    status,
                    response.Body.Length)
                : PngAuditImageRecord.FetchFailed(identity, SafeHttpFailureKind.Protocol.ToString());
        }
        if (response.BodyTruncated)
        {
            return PngAuditImageRecord.ResponseTruncated(
                identity,
                final.DisplayUrl,
                final.IdentityHash,
                response.ContentType,
                response.StatusCode.Value,
                response.Body.Length);
        }

        var analysis = await analyzer.AnalyzeAsync(
            response.Body,
            snapshot.AnalysisLimits,
            snapshot.RecommendationThresholds,
            cancellationToken);
        return PngAuditImageRecord.Analyzed(
            identity,
            final.DisplayUrl,
            final.IdentityHash,
            response.ContentType,
            response.StatusCode.Value,
            analysis);
    }

    private static PngAuditImageRecord IncompleteRecord(
        PngDiscoveredImageRequest image,
        SafeHttpTransportResult response,
        PngAuditRunSnapshot snapshot,
        PngCoverageReasonCode reason)
    {
        var identity = new PngAuditImageIdentity(image.DisplayUrl, image.IdentityHash);
        if (response.FinalRequestUrl is null)
        {
            return PngAuditImageRecord.FetchFailed(identity, reason.ToString());
        }

        var final = FinalIdentity(image, response, snapshot.UrlOptions);
        return PngAuditImageRecord.ProcessingIncomplete(
            identity,
            reason.ToString(),
            final.DisplayUrl,
            final.IdentityHash,
            response.ContentType,
            response.Body.Length);
    }

    private async Task RecordUnprocessedAsync(
        PngAuditRunClaim claim,
        IReadOnlyList<PngDiscoveredImageRequest> images,
        IReadOnlyDictionary<string, IReadOnlyList<PngImageSourceMapping>> mappingsByImage,
        string reasonCode,
        PngCoverageTracker coverage,
        PngAuditCrawlProgress crawlProgress,
        CancellationToken cancellationToken)
    {
        if (images.Count == 0) return;
        await RecordAsync(
            claim,
            new(
                images.Select(image => PngAuditImageRecord.FetchFailed(
                    new(image.DisplayUrl, image.IdentityHash),
                    reasonCode)).ToArray(),
                images.SelectMany(image => MappingsFor(image.IdentityHash, mappingsByImage)).ToArray(),
                [],
                coverage.Records,
                crawlProgress),
            cancellationToken);
    }

    private async Task RecordAsync(
        PngAuditRunClaim claim,
        PngAuditResultBatch batch,
        CancellationToken cancellationToken)
    {
        if (!await sink.RecordBatchAsync(
                claim.RunId,
                claim.LeaseToken,
                batch,
                cancellationToken))
        {
            throw new PngAuditLeaseLostException();
        }
    }

    private async Task MaintainLeaseAsync(
        PngAuditRunClaim claim,
        PngAuditLeaseState lease,
        CancellationTokenSource execution)
    {
        try
        {
            while (!execution.IsCancellationRequested)
            {
                await Task.Delay(options.HeartbeatInterval, timeProvider, execution.Token);
                if (await sink.HeartbeatAsync(
                        claim.RunId,
                        claim.LeaseToken,
                        execution.Token))
                {
                    continue;
                }

                lease.MarkLost();
                await execution.CancelAsync();
            }
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lease.MarkLost();
            logger.LogError(
                "PNG audit heartbeat failed. PngAuditRunId={PngAuditRunId} ExceptionType={ExceptionType}",
                claim.RunId,
                exception.GetType().Name);
            await execution.CancelAsync();
        }
    }

    private async Task FailTargetAsync(
        PngAuditRunClaim claim,
        PngAuditTargetState state,
        CancellationToken cancellationToken)
    {
        var code = state == PngAuditTargetState.Ineligible
            ? PngAuditFailureCodes.TargetIneligible
            : PngAuditFailureCodes.TargetChanged;
        var diagnostic = state == PngAuditTargetState.Ineligible
            ? "The endpoint is no longer eligible for a PNG audit."
            : "The endpoint target changed after the PNG audit was queued.";
        await sink.FailAsync(
            claim.RunId,
            claim.LeaseToken,
            PngAuditRunStatuses.Failed,
            code,
            diagnostic,
            cancellationToken);
    }

    private async Task TryFailAsync(
        PngAuditRunClaim claim,
        string failureCode,
        string diagnostic)
    {
        try
        {
            await sink.FailAsync(
                claim.RunId,
                claim.LeaseToken,
                PngAuditRunStatuses.Failed,
                failureCode,
                diagnostic,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "PNG audit terminal failure could not be persisted. PngAuditRunId={PngAuditRunId} ExceptionType={ExceptionType}",
                claim.RunId,
                exception.GetType().Name);
        }
    }

    private static PngAuditImageIdentity FinalIdentity(
        PngDiscoveredImageRequest image,
        SafeHttpTransportResult response,
        CrawlUrlOptions urlOptions)
    {
        var finalUrl = response.FinalRequestUrl ?? image.FetchUrl;
        var normalized = EndpointUrlNormalizer.Normalize(finalUrl);
        var identityUrl = normalized.Succeeded ? normalized.NormalizedUrl! : image.FetchUrl;
        return new(
            CrawlUrlRedactor.Redact(identityUrl, urlOptions) ?? image.DisplayUrl,
            PngAssetUrlResolver.IdentityHash(identityUrl));
    }

    private static IReadOnlyList<PngImageSourceMapping> MappingsFor(
        string imageIdentityHash,
        IReadOnlyDictionary<string, IReadOnlyList<PngImageSourceMapping>> mappingsByImage) =>
        mappingsByImage.TryGetValue(imageIdentityHash, out var mappings) ? mappings : [];

    private static void ThrowIfLeaseLost(PngAuditLeaseState lease)
    {
        if (lease.Lost) throw new PngAuditLeaseLostException();
    }

    private sealed class PngAssetRedirectPolicy(IReadOnlyList<CrawlHostRule> allowedHosts)
        : ISafeHttpRequestHopPolicy
    {
        private readonly PngAssetScope _scope = new(allowedHosts);

        public Task<SafeHttpRequestHopDecision> EvaluateAsync(
            SafeHttpRequestHop hop,
            CancellationToken cancellationToken = default)
        {
            var normalized = EndpointUrlNormalizer.Normalize(hop.Url);
            return Task.FromResult(normalized.Succeeded
                && _scope.Allows(normalized.NormalizedHost!)
                    ? new SafeHttpRequestHopDecision(true)
                    : new SafeHttpRequestHopDecision(
                        false,
                        PngCoverageReasonCode.RedirectOutOfScope.ToString()));
        }
    }

    private sealed class PngAuditLeaseState
    {
        private int _lost;

        public bool Lost => Volatile.Read(ref _lost) == 1;

        public void MarkLost() => Interlocked.Exchange(ref _lost, 1);
    }

    private sealed class PngAuditLeaseLostException : Exception;

    private sealed class PngCoverageTracker
    {
        private readonly Dictionary<(PngCoverageArea Area, PngCoverageReasonCode Reason), int> _counts;

        public PngCoverageTracker(IEnumerable<PngCoverageReason> reasons)
        {
            _counts = reasons.ToDictionary(
                reason => (reason.Area, reason.Reason),
                reason => reason.Count);
        }

        public IReadOnlyList<PngCoverageReason> Records =>
            [.. _counts.Select(item => new PngCoverageReason(
                item.Key.Area,
                item.Key.Reason,
                item.Value))];

        public void Add(
            PngCoverageArea area,
            PngCoverageReasonCode reason,
            int count = 1) =>
            _counts[(area, reason)] = checked(
                _counts.GetValueOrDefault((area, reason)) + count);
    }
}
