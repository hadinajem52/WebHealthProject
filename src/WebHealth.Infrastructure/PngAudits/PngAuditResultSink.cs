using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const string StructuredScopePrefix = "json:";

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
            Convert.ToHexString(entity.SeedUrlIdentityHash).ToLowerInvariant(),
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

    public async Task<bool> UpdateCrawlProgressAsync(
        Guid runId,
        Guid leaseToken,
        PngAuditCrawlProgress progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var updated = await database.PngAuditRuns
            .Where(run => run.Id == runId
                && run.Status == PngAuditRunStatuses.Running
                && run.LeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    run => run.PagesDiscovered,
                    run => Math.Max(
                        run.PagesDiscovered,
                        Math.Min(progress.PagesDiscovered, run.MaxPages)))
                .SetProperty(
                    run => run.HttpAttempts,
                    run => Math.Max(
                        run.HttpAttempts,
                        Math.Min(progress.HttpAttempts, run.MaxTotalHttpAttempts)))
                .SetProperty(
                    run => run.TotalPageBytes,
                    run => Math.Max(
                        run.TotalPageBytes,
                        Math.Min(progress.TotalPageBytes, run.MaxTotalPageBytes))),
                cancellationToken);
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

        var persistencePolicy = await LoadPersistencePolicyAsync(
            database, runId, cancellationToken);

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

            var entity = ToEntity(runId, image, persistencePolicy.UrlOptions, now);
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
        var sourceKeys = existingSources.Select(source => new SourceIdentity(
            source.ImageResultId,
            Convert.ToHexString(source.SourcePageIdentityHash),
            source.AttributeKind,
            source.Descriptor)).ToHashSet();
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
            if (!sourceKeys.Add(new SourceIdentity(
                    result.Id, Convert.ToHexString(sourceHash), attributeKind, descriptor)))
            {
                continue;
            }

            database.PngAuditImageSources.Add(new PngAuditImageSource
            {
                Id = Guid.CreateVersion7(),
                ImageResultId = result.Id,
                SourcePageDisplayUrl = SafeUrl(
                    mapping.SourcePageDisplayUrl, persistencePolicy.UrlOptions)!,
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
        var skipKeys = existingSkips.Select(skip => new SkipIdentity(
            Convert.ToHexString(skip.SourcePageIdentityHash),
            skip.AttributeKind,
            skip.Descriptor,
            skip.BoundedSafeRawValue,
            skip.ReasonCode)).ToHashSet();
        foreach (var skip in batch.DiscoverySkips)
        {
            var sourceHash = ParseHash(skip.SourcePageIdentityHash);
            var reasonCode = skip.Reason.ToString();
            var boundedRawValue = SafeText(
                skip.SafeRawValue, persistencePolicy.UrlOptions, PngAuditTextBounds.RawValue)!;
            var attributeKind = BoundRequired(skip.AttributeKind, PngAuditTextBounds.AttributeKind);
            var descriptor = Bound(skip.Descriptor, PngAuditTextBounds.Descriptor);
            if (!skipKeys.Add(new SkipIdentity(
                    Convert.ToHexString(sourceHash), attributeKind, descriptor,
                    boundedRawValue, reasonCode)))
            {
                continue;
            }

            database.PngAuditDiscoverySkips.Add(new PngAuditDiscoverySkipEntity
            {
                Id = Guid.CreateVersion7(),
                RunId = runId,
                SourcePageDisplayUrl = SafeUrl(
                    skip.SourcePageDisplayUrl, persistencePolicy.UrlOptions)!,
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
                reason => new CoverageIdentity(reason.Area, reason.ReasonCode),
                cancellationToken);
        foreach (var reason in batch.CoverageReasons)
        {
            if (reason.Count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batch), "Coverage counts must be positive.");
            }

            var area = reason.Area.ToString();
            var reasonCode = reason.Reason.ToString();
            var key = new CoverageIdentity(area, reasonCode);
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
        var persisted = await ReadPersistedTotalsAsync(database, runId, cancellationToken);
        ValidatePersistedLimits(persisted, batch.CrawlProgress, persistencePolicy);
        await UpdatePartialSummariesAsync(
            database, runId, leaseToken, persisted, batch.CrawlProgress, cancellationToken);
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
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var policy = await database.PngAuditRuns.AsNoTracking()
            .Where(run => run.Id == runId
                && run.Status == PngAuditRunStatuses.Running
                && run.LeaseToken == leaseToken
                && run.LeaseExpiresAt > now)
            .Select(run => new PngRunCompletionPolicy(
                run.MaxPages,
                run.MaxUniqueImages,
                run.MaxTotalHttpAttempts,
                run.MaxTotalPageBytes,
                run.MaxTotalImageBytes))
            .SingleOrDefaultAsync(cancellationToken);
        if (policy is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var persisted = await ReadPersistedTotalsAsync(database, runId, cancellationToken);
        ValidateCompletionTotals(totals, persisted, policy);
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
        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
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
        if (!PngAuditFailureCodes.IsDefined(failureCode)
            || status == PngAuditRunStatuses.Cancelled !=
                (failureCode == PngAuditFailureCodes.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(failureCode));
        }

        var now = timeProvider.GetUtcNow();
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var urlOptions = await database.PngAuditRuns.AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => new PersistedUrlOptions(
                run.QueryPolicy,
                run.TrackingQueryParameters,
                run.SensitiveQueryParameters,
                run.MaxQueryParameters))
            .SingleOrDefaultAsync(cancellationToken);
        if (urlOptions is null)
        {
            return false;
        }

        var redactionOptions = ToUrlOptions(urlOptions);
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
                SafeText(safeDiagnostic, redactionOptions, PngAuditTextBounds.Diagnostic))
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
            SeedUrlSnapshot = SafeUrl(snapshot.SeedUrl, snapshot.UrlOptions)!,
            SeedUrlIdentityHash = SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.SeedUrl)),
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
        CrawlUrlOptions urlOptions,
        DateTimeOffset recordedAt)
    {
        var facts = image.Facts;
        var comparison = image.Comparison;
        return new PngAuditImageResult
        {
            Id = Guid.CreateVersion7(),
            RunId = runId,
            ImageDisplayUrl = SafeUrl(image.Image.DisplayUrl, urlOptions)!,
            ImageIdentityHash = ParseHash(image.Image.IdentityHash),
            FinalDisplayUrl = SafeUrl(image.FinalDisplayUrl, urlOptions),
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

    private static string Scope(IEnumerable<string> values) => StructuredScopePrefix
        + JsonSerializer.Serialize(values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static IReadOnlySet<string> Set(string value) =>
        Values(value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string[] Lines(string value) => Values(value);

    private static string[] Values(string value) => value.StartsWith(
        StructuredScopePrefix, StringComparison.Ordinal)
        ? JsonSerializer.Deserialize<string[]>(value[StructuredScopePrefix.Length..]) ?? []
        : value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<PngRunPersistencePolicy> LoadPersistencePolicyAsync(
        ApplicationDbContext database,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var stored = await database.PngAuditRuns.AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => new
            {
                UrlOptions = new PersistedUrlOptions(
                    run.QueryPolicy,
                    run.TrackingQueryParameters,
                    run.SensitiveQueryParameters,
                    run.MaxQueryParameters),
                run.MaxUniqueImages,
                run.MaxTotalImageSourceMappings,
                run.MaxImageBytes,
                run.MaxTotalImageBytes,
                run.MaxPages,
                run.MaxTotalHttpAttempts,
                run.MaxTotalPageBytes
            })
            .SingleAsync(cancellationToken);
        return new(
            ToUrlOptions(stored.UrlOptions),
            stored.MaxUniqueImages,
            stored.MaxTotalImageSourceMappings,
            stored.MaxImageBytes,
            stored.MaxTotalImageBytes,
            stored.MaxPages,
            stored.MaxTotalHttpAttempts,
            stored.MaxTotalPageBytes);
    }

    private static CrawlUrlOptions ToUrlOptions(PersistedUrlOptions stored) => new()
    {
        QueryPolicy = Enum.Parse<CrawlQueryPolicy>(stored.QueryPolicy),
        TrackingParameters = Set(stored.TrackingQueryParameters),
        SensitiveQueryParameters = Set(stored.SensitiveQueryParameters),
        MaxQueryParameters = stored.MaxQueryParameters
    };

    private static async Task<PersistedResultTotals> ReadPersistedTotalsAsync(
        ApplicationDbContext database,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var images = database.PngAuditImageResults.Where(result => result.RunId == runId);
        var imageCount = await images.CountAsync(cancellationToken);
        var analyzedCount = await images.CountAsync(result =>
            result.Classification != PngAuditImageClassifications.FetchFailed
            && result.Classification != PngAuditImageClassifications.HttpNonSuccess
            && result.Classification != PngAuditImageClassifications.ResponseTruncated,
            cancellationToken);
        var recommendationCount = await images.CountAsync(result =>
            result.Recommendation == PngAuditRecommendations.LosslessWebp,
            cancellationToken);
        var totalImageBytes = await images.SumAsync(
            result => (long?)result.ResponseBytes, cancellationToken) ?? 0;
        var maximumImageBytes = await images.MaxAsync(
            result => (long?)result.ResponseBytes, cancellationToken) ?? 0;
        var imageResultIds = images.Select(result => result.Id);
        var sourceMappingCount = await database.PngAuditImageSources.CountAsync(
            source => imageResultIds.Contains(source.ImageResultId), cancellationToken);
        var discoverySkipCount = await database.PngAuditDiscoverySkips.CountAsync(
            skip => skip.RunId == runId, cancellationToken);
        var coverageAreas = await database.PngAuditCoverageReasons
            .Where(reason => reason.RunId == runId)
            .Select(reason => reason.Area)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return new(
            imageCount,
            analyzedCount,
            recommendationCount,
            discoverySkipCount,
            sourceMappingCount,
            totalImageBytes,
            maximumImageBytes,
            coverageAreas.Contains(PngCoverageArea.Crawl.ToString(), StringComparer.Ordinal),
            coverageAreas.Contains(PngCoverageArea.ImageAnalysis.ToString(), StringComparer.Ordinal),
            coverageAreas.Contains(PngCoverageArea.SourceMappings.ToString(), StringComparer.Ordinal));
    }

    private static void ValidatePersistedLimits(
        PersistedResultTotals persisted,
        PngAuditCrawlProgress crawlProgress,
        PngRunPersistencePolicy policy)
    {
        if (persisted.ImageCount > policy.MaxUniqueImages
            || persisted.SourceMappingCount > policy.MaxTotalImageSourceMappings
            || persisted.MaximumImageBytes > policy.MaxImageBytes
            || persisted.TotalImageBytes > policy.MaxTotalImageBytes
            || crawlProgress.PagesDiscovered > policy.MaxPages
            || crawlProgress.HttpAttempts > policy.MaxTotalHttpAttempts
            || crawlProgress.TotalPageBytes > policy.MaxTotalPageBytes)
        {
            throw new InvalidOperationException("PNG audit results exceed the snapshotted limits.");
        }
    }

    private static async Task UpdatePartialSummariesAsync(
        ApplicationDbContext database,
        Guid runId,
        Guid leaseToken,
        PersistedResultTotals persisted,
        PngAuditCrawlProgress crawlProgress,
        CancellationToken cancellationToken)
    {
        var updated = await database.PngAuditRuns
            .Where(run => run.Id == runId
                && run.Status == PngAuditRunStatuses.Running
                && run.LeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.PagesDiscovered, crawlProgress.PagesDiscovered)
                .SetProperty(run => run.HttpAttempts, crawlProgress.HttpAttempts)
                .SetProperty(run => run.TotalPageBytes, crawlProgress.TotalPageBytes)
                .SetProperty(run => run.ImagesDiscovered, persisted.ImageCount)
                .SetProperty(run => run.ImagesAnalyzed, persisted.AnalyzedCount)
                .SetProperty(run => run.RecommendationCount, persisted.RecommendationCount)
                .SetProperty(run => run.DiscoverySkipCount, persisted.DiscoverySkipCount)
                .SetProperty(run => run.TotalImageBytes, persisted.TotalImageBytes)
                .SetProperty(run => run.CrawlCoverageLimited, persisted.CrawlCoverageLimited)
                .SetProperty(run => run.ImageAnalysisCoverageLimited,
                    persisted.ImageAnalysisCoverageLimited)
                .SetProperty(run => run.SourceMappingCoverageLimited,
                    persisted.SourceMappingCoverageLimited),
                cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException("The PNG audit lease changed while recording results.");
        }
    }

    private static void ValidateCompletionTotals(
        PngAuditRunTotals totals,
        PersistedResultTotals persisted,
        PngRunCompletionPolicy policy)
    {
        if (totals.PagesDiscovered > policy.MaxPages
            || totals.ImagesDiscovered > policy.MaxUniqueImages
            || totals.HttpAttempts > policy.MaxTotalHttpAttempts
            || totals.TotalPageBytes > policy.MaxTotalPageBytes
            || totals.TotalImageBytes > policy.MaxTotalImageBytes)
        {
            throw new InvalidOperationException("PNG audit totals exceed the snapshotted limits.");
        }

        if (totals.ImagesDiscovered != persisted.ImageCount
            || totals.ImagesAnalyzed != persisted.AnalyzedCount
            || totals.RecommendationCount != persisted.RecommendationCount
            || totals.DiscoverySkipCount != persisted.DiscoverySkipCount
            || totals.TotalImageBytes != persisted.TotalImageBytes)
        {
            throw new InvalidOperationException(
                "PNG audit totals do not match the persisted result rows.");
        }
    }

    private static string? SafeUrl(string? value, CrawlUrlOptions urlOptions)
    {
        var cleaned = RemoveControlCharacters(value);
        return Bound(CrawlUrlRedactor.Redact(cleaned, urlOptions), CrawlUrlOptions.MaxUrlLength);
    }

    private static string? SafeText(
        string? value,
        CrawlUrlOptions urlOptions,
        int maximumLength)
    {
        var redacted = CrawlUrlRedactor.Redact(RemoveControlCharacters(value), urlOptions);
        if (redacted is null)
        {
            return null;
        }

        foreach (var parameter in urlOptions.SensitiveQueryParameters)
        {
            var pattern = $"(?<prefix>(?:^|[?&;\\s]){Regex.Escape(parameter)}=)[^&#;\\s]*";
            redacted = Regex.Replace(
                redacted,
                pattern,
                match => match.Groups["prefix"].Value + "REDACTED",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return Bound(redacted.Trim(), maximumLength);
    }

    private static string? RemoveControlCharacters(string? value) => value is null
        ? null
        : new string([.. value.Select(character => char.IsControl(character) ? ' ' : character)]);

    private static string BoundRequired(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static string? Bound(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record PersistedUrlOptions(
        string QueryPolicy,
        string TrackingQueryParameters,
        string SensitiveQueryParameters,
        int MaxQueryParameters);

    private sealed record PngRunPersistencePolicy(
        CrawlUrlOptions UrlOptions,
        int MaxUniqueImages,
        int MaxTotalImageSourceMappings,
        int MaxImageBytes,
        long MaxTotalImageBytes,
        int MaxPages,
        int MaxTotalHttpAttempts,
        long MaxTotalPageBytes);

    private sealed record PngRunCompletionPolicy(
        int MaxPages,
        int MaxUniqueImages,
        int MaxTotalHttpAttempts,
        long MaxTotalPageBytes,
        long MaxTotalImageBytes);

    private sealed record PersistedResultTotals(
        int ImageCount,
        int AnalyzedCount,
        int RecommendationCount,
        int DiscoverySkipCount,
        int SourceMappingCount,
        long TotalImageBytes,
        long MaximumImageBytes,
        bool CrawlCoverageLimited,
        bool ImageAnalysisCoverageLimited,
        bool SourceMappingCoverageLimited);

    private readonly record struct SourceIdentity(
        Guid ImageResultId,
        string SourcePageIdentityHash,
        string AttributeKind,
        string? Descriptor);

    private readonly record struct SkipIdentity(
        string SourcePageIdentityHash,
        string AttributeKind,
        string? Descriptor,
        string RawValue,
        string ReasonCode);

    private readonly record struct CoverageIdentity(string Area, string ReasonCode);
}
