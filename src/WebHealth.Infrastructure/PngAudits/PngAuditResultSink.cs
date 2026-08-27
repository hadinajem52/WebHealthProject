using Microsoft.EntityFrameworkCore;
using WebHealth.Application.PngAudits;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.PngAudits;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.PngAudits;

internal sealed class PngAuditResultSink(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    PngAuditOptions options,
    TimeProvider timeProvider) : IPngAuditResultSink
{
    public async Task CreateQueuedRunAsync(
        PngAuditQueuedRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.RunId == Guid.Empty)
        {
            throw new ArgumentException("A PNG audit run needs an identifier.", nameof(run));
        }

        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await database.PngAuditRuns.AsNoTracking()
            .Where(candidate => candidate.Id == run.RunId)
            .Select(candidate => candidate.EndpointId)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing != Guid.Empty)
        {
            if (existing != run.Snapshot.EndpointId)
            {
                throw new InvalidOperationException(
                    $"PNG audit run {run.RunId} already belongs to another endpoint.");
            }

            return;
        }

        database.PngAuditRuns.Add(ToEntity(run));
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<PngAuditRunClaim?> TryClaimAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var leaseToken = Guid.NewGuid();
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var claimed = await database.PngAuditRuns
            .Where(run => run.Id == runId
                && run.AttemptCount < options.MaximumAttempts
                && (run.Status == PngAuditRunStatuses.Queued
                    || (run.Status == PngAuditRunStatuses.Running
                        && run.LeaseExpiresAt != null
                        && run.LeaseExpiresAt < now)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, PngAuditRunStatuses.Running)
                .SetProperty(run => run.StartedAt, run => run.StartedAt ?? now)
                .SetProperty(run => run.AttemptCount, run => run.AttemptCount + 1)
                .SetProperty(run => run.LeaseToken, leaseToken)
                .SetProperty(run => run.LeaseExpiresAt, now.Add(options.LeaseDuration))
                .SetProperty(run => run.UpdatedAt, now),
                cancellationToken);
        if (claimed == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var entity = await database.PngAuditRuns.AsNoTracking()
            .SingleAsync(run => run.LeaseToken == leaseToken, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(
            entity.Id,
            leaseToken,
            entity.AttemptCount,
            ToSnapshot(entity),
            entity.QueuedAt,
            entity.StartedAt!.Value);
    }

    public async Task<bool> HeartbeatAsync(
        Guid runId,
        Guid leaseToken,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await ExtendLease(database, runId, leaseToken, now, cancellationToken);
        return updated == 1;
    }

    public async Task<bool> RecordBatchAsync(
        Guid runId,
        Guid leaseToken,
        PngAuditResultBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var now = timeProvider.GetUtcNow();
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        if (await ExtendLease(database, runId, leaseToken, now, cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var results = await database.PngAuditImageResults
            .Where(result => result.RunId == runId)
            .ToArrayAsync(cancellationToken);
        var resultsByHash = results.ToDictionary(
            result => Convert.ToHexString(result.ImageIdentityHash),
            StringComparer.OrdinalIgnoreCase);
        foreach (var image in batch.Images)
        {
            if (resultsByHash.ContainsKey(image.Image.IdentityHash))
            {
                continue;
            }

            var entity = ToEntity(runId, image, now);
            database.PngAuditImageResults.Add(entity);
            resultsByHash.Add(image.Image.IdentityHash, entity);
        }

        var resultIds = resultsByHash.Values.Select(result => result.Id).ToArray();
        var existingSources = await database.PngAuditImageSources
            .Where(source => resultIds.Contains(source.ImageResultId))
            .Select(source => new
            {
                source.ImageResultId,
                source.SourcePageIdentityHash,
                source.AttributeKind,
                source.Descriptor
            })
            .ToArrayAsync(cancellationToken);
        var sourceKeys = existingSources.Select(source => SourceKey(
            source.ImageResultId,
            source.SourcePageIdentityHash,
            source.AttributeKind,
            source.Descriptor)).ToHashSet(StringComparer.Ordinal);
        foreach (var mapping in batch.SourceMappings)
        {
            if (!resultsByHash.TryGetValue(mapping.ImageIdentityHash, out var result))
            {
                throw new InvalidOperationException(
                    "A PNG image source cannot be stored before its image result.");
            }

            var sourceHash = ParseHash(mapping.SourcePageIdentityHash);
            var attributeKind = BoundRequired(
                mapping.AttributeKind, PngAuditTextBounds.AttributeKind);
            var descriptor = Bound(mapping.Descriptor, PngAuditTextBounds.Descriptor);
            if (!sourceKeys.Add(SourceKey(
                    result.Id, sourceHash, attributeKind, descriptor)))
            {
                continue;
            }

            database.PngAuditImageSources.Add(new PngAuditImageSource
            {
                Id = Guid.CreateVersion7(),
                ImageResultId = result.Id,
                SourcePageDisplayUrl = BoundRequired(mapping.SourcePageDisplayUrl, CrawlUrlOptions.MaxUrlLength),
                SourcePageIdentityHash = sourceHash,
                AttributeKind = attributeKind,
                Descriptor = descriptor
            });
        }

        var existingSkips = await database.PngAuditDiscoverySkips
            .Where(skip => skip.RunId == runId)
            .Select(skip => new
            {
                skip.SourcePageIdentityHash,
                skip.AttributeKind,
                skip.Descriptor,
                skip.BoundedSafeRawValue,
                skip.ReasonCode
            })
            .ToArrayAsync(cancellationToken);
        var skipKeys = existingSkips.Select(skip => SkipKey(
            skip.SourcePageIdentityHash,
            skip.AttributeKind,
            skip.Descriptor,
            skip.BoundedSafeRawValue,
            skip.ReasonCode)).ToHashSet(StringComparer.Ordinal);
        foreach (var skip in batch.DiscoverySkips)
        {
            var sourceHash = ParseHash(skip.SourcePageIdentityHash);
            var reasonCode = skip.Reason.ToString();
            var boundedRawValue = BoundRequired(skip.SafeRawValue, PngAuditTextBounds.RawValue);
            var attributeKind = BoundRequired(skip.AttributeKind, PngAuditTextBounds.AttributeKind);
            var descriptor = Bound(skip.Descriptor, PngAuditTextBounds.Descriptor);
            if (!skipKeys.Add(SkipKey(
                    sourceHash, attributeKind, descriptor, boundedRawValue, reasonCode)))
            {
                continue;
            }

            database.PngAuditDiscoverySkips.Add(new PngAuditDiscoverySkipEntity
            {
                Id = Guid.CreateVersion7(),
                RunId = runId,
                SourcePageDisplayUrl = BoundRequired(skip.SourcePageDisplayUrl, CrawlUrlOptions.MaxUrlLength),
                SourcePageIdentityHash = sourceHash,
                AttributeKind = attributeKind,
                Descriptor = descriptor,
                BoundedSafeRawValue = boundedRawValue,
                ReasonCode = reasonCode,
                RecordedAt = now
            });
        }

        var existingCoverage = await database.PngAuditCoverageReasons
            .Where(reason => reason.RunId == runId)
            .ToDictionaryAsync(
                reason => reason.Area + "\n" + reason.ReasonCode,
                StringComparer.Ordinal,
                cancellationToken);
        foreach (var reason in batch.CoverageReasons)
        {
            if (reason.Count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batch), "Coverage counts must be positive.");
            }

            var area = reason.Area.ToString();
            var reasonCode = reason.Reason.ToString();
            var key = area + "\n" + reasonCode;
            if (existingCoverage.TryGetValue(key, out var stored))
            {
                stored.Count = Math.Max(stored.Count, reason.Count);
                continue;
            }

            var entity = new PngAuditCoverageReasonEntity
            {
                RunId = runId,
                Area = area,
                ReasonCode = reasonCode,
                Count = reason.Count
            };
            database.PngAuditCoverageReasons.Add(entity);
            existingCoverage.Add(key, entity);
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CompleteAsync(
        Guid runId,
        Guid leaseToken,
        PngAuditRunTotals totals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(totals);
        var now = timeProvider.GetUtcNow();
        var status = totals.CrawlCoverageLimited
            || totals.ImageAnalysisCoverageLimited
            || totals.SourceMappingCoverageLimited
            ? PngAuditRunStatuses.CompletedWithWarnings
            : PngAuditRunStatuses.Completed;
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await database.PngAuditRuns
            .Where(run => run.Id == runId
                && run.Status == PngAuditRunStatuses.Running
                && run.LeaseToken == leaseToken
                && run.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, status)
                .SetProperty(run => run.PagesDiscovered, totals.PagesDiscovered)
                .SetProperty(run => run.ImagesDiscovered, totals.ImagesDiscovered)
                .SetProperty(run => run.ImagesAnalyzed, totals.ImagesAnalyzed)
                .SetProperty(run => run.RecommendationCount, totals.RecommendationCount)
                .SetProperty(run => run.DiscoverySkipCount, totals.DiscoverySkipCount)
                .SetProperty(run => run.HttpAttempts, totals.HttpAttempts)
                .SetProperty(run => run.TotalPageBytes, totals.TotalPageBytes)
                .SetProperty(run => run.TotalImageBytes, totals.TotalImageBytes)
                .SetProperty(run => run.CrawlCoverageLimited, totals.CrawlCoverageLimited)
                .SetProperty(run => run.ImageAnalysisCoverageLimited,
                    totals.ImageAnalysisCoverageLimited)
                .SetProperty(run => run.SourceMappingCoverageLimited,
                    totals.SourceMappingCoverageLimited)
                .SetProperty(run => run.FailureCode, (string?)null)
                .SetProperty(run => run.SafeDiagnostic, (string?)null)
                .SetProperty(run => run.LeaseToken, (Guid?)null)
                .SetProperty(run => run.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(run => run.FinishedAt, now)
                .SetProperty(run => run.UpdatedAt, now),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> FailAsync(
        Guid runId,
        Guid? leaseToken,
        string status,
        string failureCode,
        string? safeDiagnostic,
        CancellationToken cancellationToken = default)
    {
        if (status is not PngAuditRunStatuses.Failed and not PngAuditRunStatuses.Cancelled)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);

        var now = timeProvider.GetUtcNow();
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = database.PngAuditRuns.Where(run => run.Id == runId);
        candidates = leaseToken is { } owned
            ? candidates.Where(run => run.Status == PngAuditRunStatuses.Running
                && run.LeaseToken == owned
                && run.LeaseExpiresAt > now)
            : candidates.Where(run => run.Status == PngAuditRunStatuses.Queued);
        var updated = await candidates.ExecuteUpdateAsync(setters => setters
            .SetProperty(run => run.Status, status)
            .SetProperty(run => run.FailureCode,
                BoundRequired(failureCode, PngAuditTextBounds.FailureCode))
            .SetProperty(run => run.SafeDiagnostic,
                Bound(safeDiagnostic, PngAuditTextBounds.Diagnostic))
            .SetProperty(run => run.LeaseToken, (Guid?)null)
            .SetProperty(run => run.LeaseExpiresAt, (DateTimeOffset?)null)
            .SetProperty(run => run.FinishedAt, now)
            .SetProperty(run => run.UpdatedAt, now),
            cancellationToken);
        return updated == 1;
    }

    private async Task<int> ExtendLease(
        ApplicationDbContext database,
        Guid runId,
        Guid leaseToken,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await database.PngAuditRuns
            .Where(run => run.Id == runId
                && run.Status == PngAuditRunStatuses.Running
                && run.LeaseToken == leaseToken
                && run.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.LeaseExpiresAt, now.Add(options.LeaseDuration))
                .SetProperty(run => run.UpdatedAt, now),
                cancellationToken);

    private static PngAuditRun ToEntity(PngAuditQueuedRun run)
    {
        var snapshot = run.Snapshot;
        var pages = snapshot.DiscoveryProfile.Pages;
        var images = snapshot.DiscoveryProfile.Images;
        var fetch = snapshot.DiscoveryProfile.Fetch;
        var analysis = snapshot.AnalysisLimits;
        return new PngAuditRun
        {
            Id = run.RunId,
            EndpointId = snapshot.EndpointId,
            Source = run.Source,
            InitiatedByUserId = run.InitiatedByUserId,
            Status = PngAuditRunStatuses.Queued,
            SeedUrlSnapshot = BoundRequired(snapshot.SeedUrl, CrawlUrlOptions.MaxUrlLength),
            IsProductionSnapshot = snapshot.IsProduction,
            AllowedPageHosts = HostScope(snapshot.AllowedPageHosts),
            AllowedPagePathPrefixes = Scope(snapshot.AllowedPagePathPrefixes),
            AllowedAssetHosts = HostScope(snapshot.AllowedAssetHosts),
            QueryPolicy = snapshot.UrlOptions.QueryPolicy.ToString(),
            TrackingQueryParameters = Scope(snapshot.UrlOptions.TrackingParameters),
            SensitiveQueryParameters = Scope(snapshot.UrlOptions.SensitiveQueryParameters),
            MaxQueryParameters = snapshot.UrlOptions.MaxQueryParameters,
            MaxPages = pages.MaxPages,
            MaxDepth = pages.MaxDepth,
            MaxPageBytes = pages.MaxPageBytes,
            MaxTotalPageBytes = pages.MaxTotalPageBytes,
            MaxImageReferencesPerPage = pages.MaxImageReferencesPerPage,
            MaxUniqueImages = images.MaxUniqueImages,
            MaxTotalImageSourceMappings = images.MaxTotalSourceMappings,
            MaxImageBytes = analysis.MaxEncodedBytes,
            MaxTotalImageBytes = snapshot.MaxTotalImageBytes,
            MaxWidth = analysis.MaxWidth,
            MaxHeight = analysis.MaxHeight,
            MaxDecodedPixels = analysis.MaxDecodedPixels,
            MaxDecodedMemoryBytes = analysis.MaxDecodedMemoryBytes,
            MaxTotalHttpAttempts = fetch.MaxTotalHttpAttempts,
            FetchTimeoutSeconds = fetch.TimeoutSeconds,
            RequestsPerSecondPerHost = fetch.RequestsPerSecondPerHost,
            TransientRetryCount = fetch.TransientRetryCount,
            MaxDurationSeconds = checked((int)Math.Ceiling(fetch.MaxDuration.TotalSeconds)),
            MinSavingsPercent = snapshot.RecommendationThresholds.MinSavingsPercent,
            MinSavingsBytes = snapshot.RecommendationThresholds.MinSavingsBytes,
            AnalyzerProfile = BoundRequired(snapshot.AnalyzerProfile, PngAuditTextBounds.Profile),
            ComparisonProfile = BoundRequired(snapshot.ComparisonProfile, PngAuditTextBounds.Profile),
            QueuedAt = run.QueuedAt,
            UpdatedAt = run.QueuedAt
        };
    }

    private static PngAuditRunSnapshot ToSnapshot(PngAuditRun run)
    {
        var urlOptions = new CrawlUrlOptions
        {
            QueryPolicy = Enum.Parse<CrawlQueryPolicy>(run.QueryPolicy),
            TrackingParameters = Set(run.TrackingQueryParameters),
            SensitiveQueryParameters = Set(run.SensitiveQueryParameters),
            MaxQueryParameters = run.MaxQueryParameters
        };
        return new(
            run.EndpointId,
            run.SeedUrlSnapshot,
            run.IsProductionSnapshot,
            HostRules(run.AllowedPageHosts),
            Lines(run.AllowedPagePathPrefixes),
            HostRules(run.AllowedAssetHosts),
            urlOptions,
            new PngSiteDiscoveryProfile(
                new PngPageDiscoveryLimits(
                    run.MaxPages,
                    run.MaxDepth,
                    run.MaxPageBytes,
                    run.MaxTotalPageBytes,
                    run.MaxImageReferencesPerPage),
                new PngImageDiscoveryLimits(
                    run.MaxUniqueImages,
                    run.MaxTotalImageSourceMappings),
                new PngDiscoveryFetchPolicy(
                    run.MaxTotalHttpAttempts,
                    run.FetchTimeoutSeconds,
                    run.RequestsPerSecondPerHost,
                    run.TransientRetryCount,
                    TimeSpan.FromSeconds(run.MaxDurationSeconds))),
            new PngImageAnalysisLimits(
                run.MaxImageBytes,
                run.MaxWidth,
                run.MaxHeight,
                run.MaxDecodedPixels,
                run.MaxDecodedMemoryBytes),
            run.MaxTotalImageBytes,
            new PngRecommendationThresholds(run.MinSavingsPercent, run.MinSavingsBytes),
            run.AnalyzerProfile,
            run.ComparisonProfile);
    }

    private static PngAuditImageResult ToEntity(
        Guid runId,
        PngAuditImageRecord image,
        DateTimeOffset recordedAt)
    {
        var facts = image.Facts;
        var comparison = image.Comparison;
        return new PngAuditImageResult
        {
            Id = Guid.CreateVersion7(),
            RunId = runId,
            ImageDisplayUrl = BoundRequired(image.Image.DisplayUrl, CrawlUrlOptions.MaxUrlLength),
            ImageIdentityHash = ParseHash(image.Image.IdentityHash),
            FinalDisplayUrl = Bound(image.FinalDisplayUrl, CrawlUrlOptions.MaxUrlLength),
            FinalIdentityHash = image.FinalIdentityHash is null
                ? null
                : ParseHash(image.FinalIdentityHash),
            DeclaredContentType = Bound(image.DeclaredContentType, PngAuditTextBounds.ContentType),
            DetectedFormat = Bound(image.DetectedFormat, PngAuditTextBounds.Format),
            HttpStatusCode = image.HttpStatusCode,
            ResponseBytes = image.ResponseBytes,
            Width = facts?.Width,
            Height = facts?.Height,
            FrameCount = facts?.FrameCount,
            PixelCount = facts?.PixelCount,
            UsesTransparency = facts?.UsesTransparency,
            TransparentPixelCount = facts?.TransparentPixelCount,
            TransparentPixelPercent = facts?.TransparentPixelPercent,
            Classification = image.Classification,
            ReasonCode = Bound(image.ReasonCode, PngAuditTextBounds.ReasonCode),
            Recommendation = image.Recommendation,
            SuggestedFormat = image.SuggestedFormat,
            NormalizedPngBytes = comparison?.NormalizedPngBytes,
            CandidateWebpBytes = comparison?.LosslessWebpBytes,
            OriginalSavingsBytes = comparison?.OriginalSavingsBytes,
            OriginalSavingsPercent = comparison?.OriginalSavingsPercent,
            NormalizedSavingsBytes = comparison?.NormalizedSavingsBytes,
            NormalizedSavingsPercent = comparison?.NormalizedSavingsPercent,
            RecordedAt = recordedAt
        };
    }

    private static byte[] ParseHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64)
        {
            throw new ArgumentException("PNG audit identity hashes must be SHA-256 values.", nameof(value));
        }

        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "PNG audit identity hashes must be hexadecimal SHA-256 values.", nameof(value), exception);
        }
    }

    private static string HostScope(IEnumerable<CrawlHostRule> rules) => Scope(
        rules.Select(rule => rule.IncludeSubdomains ? "*." + rule.Host : rule.Host));

    private static IReadOnlyList<CrawlHostRule> HostRules(string value) => Lines(value)
        .Select(item => item.StartsWith("*.", StringComparison.Ordinal)
            ? new CrawlHostRule(item[2..], true)
            : new CrawlHostRule(item))
        .ToArray();

    private static string Scope(IEnumerable<string> values) => string.Join(
        '\n', values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static IReadOnlySet<string> Set(string value) =>
        Lines(value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string[] Lines(string value) =>
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string SourceKey(
        Guid imageResultId,
        byte[] sourceHash,
        string attributeKind,
        string? descriptor) =>
        $"{imageResultId:N}|{Convert.ToHexString(sourceHash)}|{attributeKind}|{descriptor}";

    private static string SkipKey(
        byte[] sourceHash,
        string attributeKind,
        string? descriptor,
        string rawValue,
        string reasonCode) =>
        $"{Convert.ToHexString(sourceHash)}|{attributeKind}|{descriptor}|{rawValue}|{reasonCode}";

    private static string BoundRequired(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static string? Bound(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength ? value : value[..maximumLength];
}
