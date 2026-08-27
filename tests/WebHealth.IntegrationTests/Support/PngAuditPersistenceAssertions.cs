using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WebHealth.Application.PngAudits;
using WebHealth.Application.Registry;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.PngAudits;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.PngAudits;
using Xunit;

namespace WebHealth.IntegrationTests.Support;

internal static class PngAuditPersistenceAssertions
{
    public static async Task VerifyAsync(
        string connectionString,
        ApplicationDbContext database,
        IPngAuditResultSink sink,
        IPngAuditReader reader,
        IPngAuditReconciler reconciler,
        IEndpointTestGate testGate,
        Guid administratorId,
        Guid endpointId,
        string endpointUrl)
    {
        await VerifyColumnInventoryAsync(connectionString);
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(endpointId, endpointUrl);
        var runId = Guid.NewGuid();
        await sink.CreateQueuedRunAsync(new(
            runId,
            PngAuditSources.Manual,
            administratorId,
            snapshot,
            now));

        await VerifyDuplicateActiveRunRejectedAsync(
            sink, endpointId, administratorId, endpointUrl, now);

        var claim = await sink.TryClaimAsync(runId);
        claim.Should().NotBeNull();
        claim!.AttemptCount.Should().Be(1);
        claim.Snapshot.Should().BeEquivalentTo(snapshot);
        (await sink.HeartbeatAsync(runId, claim.LeaseToken)).Should().BeTrue();

        var batch = ResultBatch(endpointUrl);
        (await sink.RecordBatchAsync(runId, claim.LeaseToken, batch)).Should().BeTrue();
        (await sink.RecordBatchAsync(runId, claim.LeaseToken, batch)).Should().BeTrue();

        database.ChangeTracker.Clear();
        (await database.PngAuditImageResults.CountAsync(result => result.RunId == runId))
            .Should().Be(15, "every legal result state should be persisted once");
        var candidate = await database.PngAuditImageResults.AsNoTracking()
            .SingleAsync(result => result.RunId == runId
                && result.Classification == PngAuditImageClassifications.OpaqueWebpCandidate);
        var collisionBatch = CollisionBatch(candidate.ImageIdentityHash, endpointUrl);
        (await sink.RecordBatchAsync(runId, claim.LeaseToken, collisionBatch)).Should().BeTrue();
        (await sink.RecordBatchAsync(runId, claim.LeaseToken, collisionBatch)).Should().BeTrue();
        (await database.PngAuditImageSources.CountAsync(source => source.ImageResultId == candidate.Id))
            .Should().Be(6, "structurally distinct source mappings must not collide");
        (await database.PngAuditDiscoverySkips.CountAsync(skip => skip.RunId == runId))
            .Should().Be(5, "structurally distinct discovery skips must not collide");
        (await database.PngAuditCoverageReasons.SingleAsync(reason =>
            reason.RunId == runId
            && reason.Area == PngCoverageArea.Crawl.ToString()
            && reason.ReasonCode == PngCoverageReasonCode.PageLimit.ToString()))
            .Count.Should().Be(2, "replayed batches use accumulated coverage counts idempotently");

        await VerifyDuplicateImageIdentityRejectedAsync(database, candidate);
        await VerifyStateInvariantsAsync(connectionString, runId);

        database.ChangeTracker.Clear();
        var partial = await database.PngAuditRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
        partial.ImagesDiscovered.Should().Be(15);
        partial.ImagesAnalyzed.Should().Be(12);
        partial.RecommendationCount.Should().Be(1);
        partial.DiscoverySkipCount.Should().Be(5);
        partial.PagesDiscovered.Should().Be(3);
        partial.HttpAttempts.Should().Be(18, "run-level crawl budgets are checkpointed with results");
        partial.TotalPageBytes.Should().Be(4096);
        partial.TotalImageBytes.Should().Be(batch.Images.Sum(image => image.ResponseBytes));
        partial.CrawlCoverageLimited.Should().BeTrue();
        partial.ImageAnalysisCoverageLimited.Should().BeTrue();
        partial.SourceMappingCoverageLimited.Should().BeFalse();

        var totalImageBytes = batch.Images.Sum(image => image.ResponseBytes);
        var totals = new PngAuditRunTotals(
            3,
            15,
            12,
            1,
            5,
            18,
            4096,
            totalImageBytes,
            true,
            true,
            false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.CompleteAsync(
            runId,
            claim.LeaseToken,
            new PngAuditRunTotals(
                3, 14, 12, 1, 5, 18, 4096, totalImageBytes, true, true, false)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.CompleteAsync(
            runId,
            claim.LeaseToken,
            new PngAuditRunTotals(
                21, 15, 12, 1, 5, 18, 4096, totalImageBytes, true, true, false)));
        (await sink.CompleteAsync(runId, claim.LeaseToken, totals)).Should().BeTrue();
        (await sink.HeartbeatAsync(runId, claim.LeaseToken)).Should().BeFalse(
            "a terminal run no longer has a lease");

        var access = new RegistryAccessContext(
            administratorId,
            [ApplicationRoles.Administrator]);
        var storedRun = await reader.FindRunAsync(runId, access);
        storedRun.Should().NotBeNull();
        storedRun!.Status.Should().Be(PngAuditRunStatuses.CompletedWithWarnings);
        storedRun.ImagesDiscovered.Should().Be(15);
        storedRun.DiscoverySkipCount.Should().Be(5);
        var firstPage = await reader.ListImagesAsync(runId, 0, 5, access);
        firstPage.Items.Should().HaveCount(5);
        firstPage.HasMore.Should().BeTrue();
        var secondPage = await reader.ListImagesAsync(runId, 5, 50, access);
        secondPage.Items.Should().HaveCount(10);
        secondPage.HasMore.Should().BeFalse();
        var storedCandidate = firstPage.Items.Concat(secondPage.Items)
            .Single(image => image.Classification == PngAuditImageClassifications.OpaqueWebpCandidate);
        storedCandidate.CandidateWebpBytes.Should().Be(700);
        storedCandidate.NormalizedSavingsBytes.Should().Be(200);
        storedCandidate.NormalizedSavingsPercent.Should().Be(22.2222m);
        storedCandidate.SourceCount.Should().Be(6);
        storedCandidate.FirstSourcePageDisplayUrl.Should().Be(endpointUrl + "/");
        var candidatePage = await reader.ListImagesByFilterAsync(
            runId, PngAuditImageFilters.WebpCandidates, 0, 50, access);
        candidatePage.Items.Should().ContainSingle()
            .Which.Classification.Should().Be(PngAuditImageClassifications.OpaqueWebpCandidate);
        var notAnalyzedPage = await reader.ListImagesByFilterAsync(
            runId, PngAuditImageFilters.NotAnalyzed, 0, 50, access);
        notAnalyzedPage.Items.Should().HaveCount(9);
        notAnalyzedPage.Items.Should().NotContain(
            image => image.Classification == PngAuditImageClassifications.WebpComparisonFailed,
            "a failed WebP comparison still decoded and inspected the PNG");
        var notPngPage = await reader.ListImagesByFilterAsync(
            runId, PngAuditImageFilters.NotPng, 0, 50, access);
        notPngPage.Items.Should().ContainSingle();
        var resultSummary = await reader.GetResultSummaryAsync(runId, access);
        resultSummary.Should().Be(new PngAuditResultSummaryView(
            11,
            15,
            5,
            1,
            3,
            1,
            1,
            15));
        (notAnalyzedPage.Items.Count + notPngPage.Items.Count).Should().Be(
            resultSummary.SkippedOrNotAnalyzed - storedRun.DiscoverySkipCount,
            "the two non-analyzed filters must partition the images the summary excludes");
        (await reader.ListSourcesAsync(candidate.Id, 0, 10, access)).Items.Should().HaveCount(6);
        (await reader.ListDiscoverySkipsAsync(runId, 0, 10, access)).Items.Should().HaveCount(5);
        (await reader.ListCoverageReasonsAsync(runId, access)).Should().HaveCount(2);

        await VerifyBoundaryHardeningAsync(
            database, sink, reader, endpointId, administratorId, endpointUrl, access);
        await VerifyLeaseRecoveryAndAttemptLimitAsync(
            database, sink, reconciler, endpointId, administratorId, endpointUrl);
        await VerifyQueueFailureRetiresRunAsync(
            database,
            sink,
            testGate,
            endpointId,
            access);
        await VerifyLeaseRecoveryBudgetCarryOverAsync(
            database, sink, endpointId, administratorId, endpointUrl);
        await VerifyEndToEndExecutionAsync(
            database,
            sink,
            endpointId,
            administratorId,
            endpointUrl);
    }

    public static void SeedPurgeFixture(
        ApplicationDbContext database,
        Guid endpointId,
        DateTimeOffset now)
    {
        var runId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        database.PngAuditRuns.Add(new PngAuditRun
        {
            Id = runId,
            EndpointId = endpointId,
            Source = PngAuditSources.Scheduled,
            Status = PngAuditRunStatuses.CompletedWithWarnings,
            SeedUrlSnapshot = "https://endpoint-purge.test/",
            SeedUrlIdentityHash = HashBytes("https://endpoint-purge.test/"),
            IsProductionSnapshot = true,
            AllowedPageHosts = "endpoint-purge.test",
            AllowedPagePathPrefixes = string.Empty,
            AllowedAssetHosts = "endpoint-purge.test",
            QueryPolicy = CrawlQueryPolicy.Canonicalize.ToString(),
            TrackingQueryParameters = "utm_source",
            SensitiveQueryParameters = "token",
            MaxQueryParameters = 12,
            MaxPages = 200,
            MaxDepth = 5,
            MaxPageBytes = 1024 * 1024,
            MaxTotalPageBytes = 32L * 1024 * 1024,
            MaxImageReferencesPerPage = 1000,
            MaxUniqueImages = 500,
            MaxTotalImageSourceMappings = 5000,
            MaxImageBytes = 8 * 1024 * 1024,
            MaxTotalImageBytes = 128L * 1024 * 1024,
            MaxWidth = 10000,
            MaxHeight = 10000,
            MaxDecodedPixels = 40000000,
            MaxDecodedMemoryBytes = 256L * 1024 * 1024,
            MaxTotalHttpAttempts = 1500,
            FetchTimeoutSeconds = 15,
            RequestsPerSecondPerHost = 1,
            TransientRetryCount = 1,
            MaxDurationSeconds = 1800,
            MinSavingsPercent = 10,
            MinSavingsBytes = 4096,
            AnalyzerProfile = PngAnalysisProfiles.Analyzer,
            ComparisonProfile = PngAnalysisProfiles.Comparison,
            PagesDiscovered = 1,
            ImagesDiscovered = 1,
            ImagesAnalyzed = 0,
            RecommendationCount = 0,
            DiscoverySkipCount = 1,
            HttpAttempts = 2,
            TotalPageBytes = 1000,
            TotalImageBytes = 0,
            CrawlCoverageLimited = true,
            AttemptCount = 1,
            QueuedAt = now.AddMinutes(-2),
            StartedAt = now.AddMinutes(-1),
            UpdatedAt = now,
            FinishedAt = now
        });
        database.PngAuditImageResults.Add(new PngAuditImageResult
        {
            Id = resultId,
            RunId = runId,
            ImageDisplayUrl = "https://endpoint-purge.test/logo.png",
            ImageIdentityHash = HashBytes("purge-image"),
            ResponseBytes = 0,
            Classification = PngAuditImageClassifications.FetchFailed,
            ReasonCode = "ConnectionFailed",
            Recommendation = PngAuditRecommendations.None,
            RecordedAt = now
        });
        database.PngAuditImageSources.Add(new PngAuditImageSource
        {
            Id = Guid.NewGuid(),
            ImageResultId = resultId,
            SourcePageDisplayUrl = "https://endpoint-purge.test/",
            SourcePageIdentityHash = HashBytes("purge-source"),
            AttributeKind = "ImgSrc"
        });
        database.PngAuditDiscoverySkips.Add(new PngAuditDiscoverySkipEntity
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            SourcePageDisplayUrl = "https://endpoint-purge.test/",
            SourcePageIdentityHash = HashBytes("purge-source"),
            AttributeKind = "ImgSrc",
            BoundedSafeRawValue = "data:image/png;base64,REDACTED",
            ReasonCode = "DataUrl",
            RecordedAt = now
        });
        database.PngAuditCoverageReasons.Add(new PngAuditCoverageReasonEntity
        {
            RunId = runId,
            Area = PngCoverageArea.Crawl.ToString(),
            ReasonCode = PngCoverageReasonCode.PageLimit.ToString(),
            Count = 1
        });
    }

    private static PngAuditRunSnapshot Snapshot(
        Guid endpointId,
        string endpointUrl,
        IReadOnlyList<string>? pathPrefixes = null,
        bool isProduction = true) => new(
        endpointId,
        endpointUrl,
        isProduction,
        [new CrawlHostRule(new Uri(endpointUrl).Host)],
        pathPrefixes ?? [],
        [new CrawlHostRule(new Uri(endpointUrl).Host)],
        CrawlUrlOptions.Default,
        new PngSiteDiscoveryProfile(
            new PngPageDiscoveryLimits(20, 3, 1024 * 1024, 4L * 1024 * 1024, 100),
            new PngImageDiscoveryLimits(50, 200),
            new PngDiscoveryFetchPolicy(200, 15, 1, 1, TimeSpan.FromMinutes(10))),
        new PngImageAnalysisLimits(8 * 1024 * 1024, 10000, 10000, 40000000, 256L * 1024 * 1024),
        32L * 1024 * 1024,
        new PngRecommendationThresholds(10, 100),
        PngAnalysisProfiles.Analyzer,
        PngAnalysisProfiles.Comparison);

    private static PngAuditResultBatch ResultBatch(string endpointUrl)
    {
        var factsOpaque = new PngImageFacts(10, 10, 1, 100, 0);
        var candidate = PngAnalysisResult.Compared(
            factsOpaque,
            new PngComparisonMetrics(1000, 900, 700),
            new PngRecommendationThresholds(10, 100));
        var below = PngAnalysisResult.Compared(
            factsOpaque,
            new PngComparisonMetrics(1000, 900, 850),
            new PngRecommendationThresholds(20, 200));
        var analyses = new PngAnalysisResult[]
        {
            PngAnalysisResult.NotPng(500, "JPEG"),
            PngAnalysisResult.Failed(PngImageAnalysisClassification.IdentificationFailed, 600),
            PngAnalysisResult.Failed(PngImageAnalysisClassification.UnsupportedBitDepth, 700),
            PngAnalysisResult.Failed(PngImageAnalysisClassification.DimensionsExceeded, 800),
            PngAnalysisResult.Failed(PngImageAnalysisClassification.PixelLimitExceeded, 900),
            PngAnalysisResult.Failed(PngImageAnalysisClassification.DecodedMemoryExceeded, 1000),
            PngAnalysisResult.Animated(1100, new PngImageFacts(10, 10, 2, 100, null)),
            PngAnalysisResult.Failed(PngImageAnalysisClassification.DecodeFailed, 1200),
            PngAnalysisResult.Transparent(1300, new PngImageFacts(10, 10, 1, 100, 10)),
            PngAnalysisResult.ComparisonFailed(1400, factsOpaque),
            candidate,
            below
        };
        var images = new List<PngAuditImageRecord>
        {
            PngAuditImageRecord.FetchFailed(Identity(endpointUrl, "fetch-failed"), "ConnectionFailed"),
            PngAuditImageRecord.HttpNonSuccess(
                Identity(endpointUrl, "http-failed"),
                endpointUrl + "/http-failed.png",
                Hash("final-http-failed"),
                "image/png",
                404,
                100),
            PngAuditImageRecord.ResponseTruncated(
                Identity(endpointUrl, "truncated"),
                endpointUrl + "/truncated.png",
                Hash("final-truncated"),
                "image/png",
                200,
                8 * 1024 * 1024)
        };
        images.AddRange(analyses.Select((analysis, index) => PngAuditImageRecord.Analyzed(
            Identity(endpointUrl, "analysis-" + index),
            endpointUrl + "/analysis-" + index + ".png",
            Hash("final-analysis-" + index),
            "image/png",
            200,
            analysis)));

        var candidateIdentity = images.Single(image =>
            image.Classification == PngAuditImageClassifications.OpaqueWebpCandidate).Image.IdentityHash;
        return new(
            images,
            [
                new(candidateIdentity, endpointUrl + "/", Hash("source-1"), "ImgSrc", null),
                new(candidateIdentity, endpointUrl + "/gallery", Hash("source-2"), "ImgSrcset", "2x")
            ],
            [new(endpointUrl + "/", Hash("source-1"), "ImgSrc", null, "data:image/png", PngDiscoverySkipReason.DataUrl)],
            [
                new(PngCoverageArea.Crawl, PngCoverageReasonCode.PageLimit, 2),
                new(PngCoverageArea.ImageAnalysis, PngCoverageReasonCode.TotalImageBytesLimit, 1)
            ],
            new(3, 18, 4096));
    }

    private static PngAuditResultBatch CollisionBatch(byte[] imageIdentityHash, string endpointUrl)
    {
        var imageHash = Convert.ToHexString(imageIdentityHash);
        var sourceUrl = endpointUrl + "/collision";
        var sourceHash = Hash("collision-source");
        return new(
            [],
            [
                new(imageHash, sourceUrl, sourceHash, "A|B", "C"),
                new(imageHash, sourceUrl, sourceHash, "A", "B|C"),
                new(imageHash, sourceUrl, sourceHash, "NullDescriptor", null),
                new(imageHash, sourceUrl, sourceHash, "NullDescriptor", string.Empty)
            ],
            [
                new(sourceUrl, sourceHash, "A|B", "C", "D", PngDiscoverySkipReason.DataUrl),
                new(sourceUrl, sourceHash, "A", "B|C", "D", PngDiscoverySkipReason.DataUrl),
                new(sourceUrl, sourceHash, "NullDescriptor", null, "D", PngDiscoverySkipReason.DataUrl),
                new(sourceUrl, sourceHash, "NullDescriptor", string.Empty, "D", PngDiscoverySkipReason.DataUrl)
            ],
            [],
            new(3, 18, 4096));
    }

    private static async Task VerifyBoundaryHardeningAsync(
        ApplicationDbContext database,
        IPngAuditResultSink sink,
        IPngAuditReader reader,
        Guid endpointId,
        Guid administratorId,
        string endpointUrl,
        RegistryAccessContext access)
    {
        var seedUrl = endpointUrl.TrimEnd('/') + "/?token=seed-secret&view=full";
        var pathPrefix = "/tenant-a\n/";
        var runId = Guid.NewGuid();
        await sink.CreateQueuedRunAsync(new(
            runId,
            PngAuditSources.Manual,
            administratorId,
            Snapshot(endpointId, seedUrl, [pathPrefix]),
            DateTimeOffset.UtcNow));

        database.ChangeTracker.Clear();
        var queued = await database.PngAuditRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
        queued.SeedUrlSnapshot.Should().Contain("token=REDACTED").And.NotContain("seed-secret");
        queued.SeedUrlIdentityHash.Should().Equal(HashBytes(seedUrl));

        var claim = await sink.TryClaimAsync(runId);
        claim.Should().NotBeNull();
        claim!.Snapshot.AllowedPagePathPrefixes.Should().Equal(pathPrefix);
        claim.SeedIdentityHash.Should().Be(Hash(seedUrl));

        var imageUrl = endpointUrl + "/secret.png?token=image-secret";
        var sourceUrl = endpointUrl + "/source?token=source-secret";
        var rawValue = endpointUrl + "/skipped.png?token=skip-secret";
        var image = PngAuditImageRecord.FetchFailed(
            new PngAuditImageIdentity(imageUrl, Hash(imageUrl)),
            "ConnectionFailed");
        var batch = new PngAuditResultBatch(
            [image],
            [new(image.Image.IdentityHash, sourceUrl, Hash(sourceUrl), "ImgSrc", null)],
            [new(sourceUrl, Hash(sourceUrl), "ImgSrc", null, rawValue, PngDiscoverySkipReason.MalformedUrl)],
            [],
            new(1, 1, 0));
        (await sink.RecordBatchAsync(runId, claim.LeaseToken, batch)).Should().BeTrue();

        database.ChangeTracker.Clear();
        var storedImage = await database.PngAuditImageResults.AsNoTracking()
            .SingleAsync(result => result.RunId == runId);
        var storedSource = await database.PngAuditImageSources.AsNoTracking()
            .SingleAsync(source => source.ImageResultId == storedImage.Id);
        var storedSkip = await database.PngAuditDiscoverySkips.AsNoTracking()
            .SingleAsync(skip => skip.RunId == runId);
        storedImage.ImageDisplayUrl.Should().Contain("token=REDACTED").And.NotContain("image-secret");
        storedSource.SourcePageDisplayUrl.Should().Contain("token=REDACTED").And.NotContain("source-secret");
        storedSkip.SourcePageDisplayUrl.Should().Contain("token=REDACTED").And.NotContain("source-secret");
        storedSkip.BoundedSafeRawValue.Should().Contain("token=REDACTED").And.NotContain("skip-secret");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sink.FailAsync(
            runId,
            claim.LeaseToken,
            PngAuditRunStatuses.Failed,
            "MisspelledFailure",
            null));
        (await sink.FailAsync(
            runId,
            claim.LeaseToken,
            PngAuditRunStatuses.Failed,
            PngAuditFailureCodes.Unexpected,
            $"Request failed for {sourceUrl}\nworker detail")).Should().BeTrue();

        var view = await reader.FindRunAsync(runId, access);
        view.Should().NotBeNull();
        view!.ImagesDiscovered.Should().Be(1);
        view.ImagesAnalyzed.Should().Be(0);
        view.DiscoverySkipCount.Should().Be(1);
        view.SafeDiagnostic.Should().Contain("token=REDACTED").And.NotContain("source-secret");
        view.SafeDiagnostic.Should().NotContain("\n").And.NotContain("\r");
    }

    private static async Task VerifyDuplicateActiveRunRejectedAsync(
        IPngAuditResultSink sink,
        Guid endpointId,
        Guid administratorId,
        string endpointUrl,
        DateTimeOffset now)
    {
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => sink.CreateQueuedRunAsync(new(
            Guid.NewGuid(),
            PngAuditSources.Manual,
            administratorId,
            Snapshot(endpointId, endpointUrl),
            now)));
        duplicate.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ux_png_audit_run_active");
    }

    private static async Task VerifyLeaseRecoveryAndAttemptLimitAsync(
        ApplicationDbContext database,
        IPngAuditResultSink sink,
        IPngAuditReconciler reconciler,
        Guid endpointId,
        Guid administratorId,
        string endpointUrl)
    {
        var runId = Guid.NewGuid();
        await sink.CreateQueuedRunAsync(new(
            runId,
            PngAuditSources.Manual,
            administratorId,
            Snapshot(endpointId, endpointUrl),
            DateTimeOffset.UtcNow.AddMinutes(-5)));
        var firstClaim = await sink.TryClaimAsync(runId);
        firstClaim.Should().NotBeNull();
        await database.PngAuditRuns.Where(run => run.Id == runId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))
                .SetProperty(run => run.UpdatedAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        (await sink.CompleteAsync(runId, firstClaim!.LeaseToken, new(
            0, 0, 0, 0, 0, 0, 0, 0, false, false, false))).Should().BeFalse(
            "an expired lease cannot publish a terminal result");
        var secondClaim = await sink.TryClaimAsync(runId);
        secondClaim.Should().NotBeNull();
        secondClaim!.LeaseToken.Should().NotBe(firstClaim.LeaseToken);
        (await sink.HeartbeatAsync(runId, firstClaim.LeaseToken)).Should().BeFalse(
            "a stale worker cannot extend a replacement lease");

        await database.PngAuditRuns.Where(run => run.Id == runId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.AttemptCount, 3)
                .SetProperty(run => run.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))
                .SetProperty(run => run.UpdatedAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        (await reconciler.ReconcileAsync()).Should().NotContain(runId);
        database.ChangeTracker.Clear();
        var exhausted = await database.PngAuditRuns.AsNoTracking()
            .SingleAsync(run => run.Id == runId);
        exhausted.Status.Should().Be(PngAuditRunStatuses.Failed);
        exhausted.FailureCode.Should().Be(PngAuditFailureCodes.AttemptsExhausted);
        exhausted.LeaseToken.Should().BeNull();
    }

    private static async Task VerifyQueueFailureRetiresRunAsync(
        ApplicationDbContext database,
        IPngAuditResultSink sink,
        IEndpointTestGate testGate,
        Guid endpointId,
        RegistryAccessContext access)
    {
        var runner = new PngAuditRunner(
            database,
            sink,
            testGate,
            new PngAuditOptions { Enabled = true },
            TimeProvider.System,
            NullLogger<PngAuditRunner>.Instance,
            new FailingPngAuditQueue());

        var result = await runner.QueueManualAsync(endpointId, access);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
        database.ChangeTracker.Clear();
        var failed = await database.PngAuditRuns.AsNoTracking()
            .Where(run => run.EndpointId == endpointId)
            .OrderByDescending(run => run.QueuedAt)
            .ThenByDescending(run => run.Id)
            .FirstAsync();
        failed.Status.Should().Be(PngAuditRunStatuses.Failed);
        failed.FailureCode.Should().Be(PngAuditFailureCodes.WorkerUnavailable);
    }

    private static async Task VerifyLeaseRecoveryBudgetCarryOverAsync(
        ApplicationDbContext database,
        IPngAuditResultSink sink,
        Guid endpointId,
        Guid administratorId,
        string endpointUrl)
    {
        var isProduction = await database.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.Id == endpointId)
            .Select(endpoint => endpoint.Environment.IsProduction)
            .SingleAsync();
        var seedUrl = CrawlUrlNormalizer.Normalize(endpointUrl, CrawlUrlOptions.Default).Url!.Value;
        var snapshot = Snapshot(endpointId, seedUrl, isProduction: isProduction);
        var runId = Guid.NewGuid();
        await sink.CreateQueuedRunAsync(new(
            runId,
            PngAuditSources.Manual,
            administratorId,
            snapshot,
            DateTimeOffset.UtcNow));

        var abandoned = await sink.TryClaimAsync(runId);
        abandoned.Should().NotBeNull();
        (await sink.RecordBatchAsync(
            runId,
            abandoned!.LeaseToken,
            new([], [], [], [], new(5, 40, 100_000)))).Should().BeTrue();

        await database.PngAuditRuns
            .Where(run => run.Id == runId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        database.ChangeTracker.Clear();
        var expired = await database.PngAuditRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
        expired.HttpAttempts.Should().Be(40, "the abandoned attempt checkpointed its crawl budget");
        expired.TotalPageBytes.Should().Be(100_000);
        expired.PagesDiscovered.Should().Be(5);

        var crawler = new RecordingPngCrawler(new(
            [new(seedUrl, Hash(seedUrl), 0)],
            [],
            [],
            [],
            [],
            42,
            100_128));
        var execution = new PngAuditExecutionService(
            sink,
            new PngAuditQueuedRunReader(database),
            crawler,
            new CompletedPngImageTransport(seedUrl),
            new SnapshotPngAnalyzer(),
            new PngAuditOptions(),
            TimeProvider.System,
            NullLogger<PngAuditExecutionService>.Instance);

        await execution.ExecuteAsync(runId, CancellationToken.None);

        crawler.Request.Should().NotBeNull("the expired lease must be reclaimed");
        crawler.Request!.ConsumedHttpAttempts.Should().Be(
            40, "a recovered attempt may only spend the remaining HTTP budget");
        crawler.Request.ConsumedPageBytes.Should().Be(
            100_000, "a recovered attempt may only spend the remaining page-byte budget");
        crawler.Request.RunDeadline.Should().Be(
            expired.StartedAt!.Value + snapshot.DiscoveryProfile.Fetch.MaxDuration,
            "the wall-clock budget runs from the original start, not from the new attempt");

        database.ChangeTracker.Clear();
        var completed = await database.PngAuditRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
        completed.AttemptCount.Should().Be(2);
        completed.HttpAttempts.Should().Be(42, "run totals stay cumulative across attempts");
        completed.TotalPageBytes.Should().Be(100_128);
        completed.PagesDiscovered.Should().Be(5, "the recovered crawl re-walked the same page space");
    }

    private static async Task VerifyEndToEndExecutionAsync(
        ApplicationDbContext database,
        IPngAuditResultSink sink,
        Guid endpointId,
        Guid administratorId,
        string endpointUrl)
    {
        var isProduction = await database.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.Id == endpointId)
            .Select(endpoint => endpoint.Environment.IsProduction)
            .SingleAsync();
        var seedUrl = CrawlUrlNormalizer.Normalize(endpointUrl, CrawlUrlOptions.Default).Url!.Value;
        var runId = Guid.NewGuid();
        await sink.CreateQueuedRunAsync(new(
            runId,
            PngAuditSources.Manual,
            administratorId,
            Snapshot(endpointId, seedUrl, isProduction: isProduction),
            DateTimeOffset.UtcNow));
        var imageUrl = seedUrl.TrimEnd('/') + "/asset.png";
        var imageHash = Hash(imageUrl);
        var crawler = new CompletedPngCrawler(new(
            [new(seedUrl, Hash(seedUrl), 0)],
            [new(imageUrl, imageUrl, imageHash)],
            [new(imageHash, seedUrl, Hash(seedUrl), "img.src", null)],
            [],
            [],
            1,
            128));
        var analyzer = new SnapshotPngAnalyzer();
        var execution = new PngAuditExecutionService(
            sink,
            new PngAuditQueuedRunReader(database),
            crawler,
            new CompletedPngImageTransport(imageUrl),
            analyzer,
            new PngAuditOptions(),
            TimeProvider.System,
            NullLogger<PngAuditExecutionService>.Instance);

        await execution.ExecuteAsync(runId, CancellationToken.None);

        database.ChangeTracker.Clear();
        var completed = await database.PngAuditRuns.AsNoTracking()
            .SingleAsync(run => run.Id == runId);
        completed.Status.Should().Be(
            PngAuditRunStatuses.Completed,
            $"{completed.FailureCode}: {completed.SafeDiagnostic}");
        completed.PagesDiscovered.Should().Be(1);
        completed.ImagesDiscovered.Should().Be(1);
        completed.ImagesAnalyzed.Should().Be(1);
        completed.HttpAttempts.Should().Be(2);
        completed.TotalPageBytes.Should().Be(128);
        completed.TotalImageBytes.Should().Be(1);
        analyzer.SnapshotCallCount.Should().Be(1);
    }

    private static async Task VerifyDuplicateImageIdentityRejectedAsync(
        ApplicationDbContext database,
        PngAuditImageResult existing)
    {
        database.PngAuditImageResults.Add(new PngAuditImageResult
        {
            Id = Guid.NewGuid(),
            RunId = existing.RunId,
            ImageDisplayUrl = existing.ImageDisplayUrl,
            ImageIdentityHash = existing.ImageIdentityHash,
            ResponseBytes = 0,
            Classification = PngAuditImageClassifications.FetchFailed,
            ReasonCode = "ConnectionFailed",
            Recommendation = PngAuditRecommendations.None,
            RecordedAt = DateTimeOffset.UtcNow
        });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        duplicate.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ux_png_audit_image_result_identity");
        database.ChangeTracker.Clear();
    }

    private sealed class FailingPngAuditQueue : IPngAuditRunQueue
    {
        public void Enqueue(Guid runId) => throw new InvalidOperationException("Queue unavailable.");
    }

    private sealed class RecordingPngCrawler(PngSiteDiscoveryResult result) : IPngSiteCrawler
    {
        public PngSiteCrawlRequest? Request { get; private set; }

        public Task<PngSiteDiscoveryResult> DiscoverAsync(
            PngSiteCrawlRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class CompletedPngCrawler(PngSiteDiscoveryResult result) : IPngSiteCrawler
    {
        public Task<PngSiteDiscoveryResult> DiscoverAsync(
            PngSiteCrawlRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class CompletedPngImageTransport(string finalUrl) : IPngImageTransport
    {
        public Task<SafeHttpTransportResult> SendAsync(
            PngImageTransportRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SafeHttpTransportResult(
                null,
                200,
                new(finalUrl),
                TimeSpan.Zero,
                1,
                false,
                new byte[] { 1 },
                [],
                ContentType: "image/png")
            {
                FinalRequestUrl = finalUrl,
                OutboundRequestCount = 1
            });
    }

    private sealed class SnapshotPngAnalyzer : IPngImageAnalyzer
    {
        public int SnapshotCallCount { get; private set; }

        public Task<PngAnalysisResult> AnalyzeAsync(
            ReadOnlyMemory<byte> encodedImage,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Execution must use the snapshotted policy.");

        public Task<PngAnalysisResult> AnalyzeAsync(
            ReadOnlyMemory<byte> encodedImage,
            PngImageAnalysisLimits limits,
            PngRecommendationThresholds recommendationThresholds,
            CancellationToken cancellationToken = default)
        {
            SnapshotCallCount++;
            return Task.FromResult(PngAnalysisResult.NotPng(encodedImage.Length, "Webp"));
        }
    }

    private static async Task VerifyStateInvariantsAsync(string connectionString, Guid runId)
    {
        await ExpectConstraintAsync(connectionString, ResultInsert(
            runId, "animated-invalid", "AnimatedPng",
            "200, 1000, 10, 10, 1, 100, FALSE, 0, 0"),
            "ck_png_audit_image_result_state");
        await ExpectConstraintAsync(connectionString, ResultInsert(
            runId, "animated-missing-facts", "AnimatedPng",
            "200, 1000, NULL, NULL, NULL, NULL, NULL, NULL, NULL"),
            "ck_png_audit_image_result_state");
        await ExpectConstraintAsync(connectionString, ResultInsert(
            runId, "transparent-invalid", "UsesTransparency",
            "200, 1000, 10, 10, 1, 100, FALSE, 0, 0"),
            "ck_png_audit_image_result_state");
        await ExpectConstraintAsync(connectionString, ResultInsert(
            runId, "not-png-invalid", "NotPng",
            "200, 1000, NULL, NULL, NULL, NULL, NULL, NULL, NULL",
            "'PNG'"),
            "ck_png_audit_image_result_state");
        await ExpectConstraintAsync(connectionString, ResultInsert(
            runId, "transport-invalid", "NotPng",
            "NULL, 1000, NULL, NULL, NULL, NULL, NULL, NULL, NULL",
            "'JPEG'"),
            "ck_png_audit_image_result_transport");
        await ExpectConstraintAsync(connectionString, ResultInsert(
            runId, "transparency-invalid", "UsesTransparency",
            "200, 1000, 10, 10, 1, 100, TRUE, 0, 0"),
            "ck_png_audit_image_result_transparency");
        await ExpectConstraintAsync(connectionString,
            $"UPDATE web_health.png_audit_image_result SET height = NULL "
            + $"WHERE run_id = '{runId}' AND classification = 'UsesTransparency';",
            "ck_png_audit_image_result_facts");
        await ExpectConstraintAsync(connectionString,
            $"UPDATE web_health.png_audit_image_result SET original_savings_percent = 0 "
            + $"WHERE run_id = '{runId}' AND classification = 'OpaqueWebpCandidate';",
            "ck_png_audit_image_result_comparison");
        await ExpectConstraintAsync(connectionString,
            $"UPDATE web_health.png_audit_image_result SET suggested_format = NULL "
            + $"WHERE run_id = '{runId}' AND classification = 'OpaqueWebpCandidate';",
            "ck_png_audit_image_result_recommendation");
        await ExpectConstraintAsync(connectionString,
            $"UPDATE web_health.png_audit_image_result SET final_identity_hash = NULL "
            + $"WHERE run_id = '{runId}' AND classification = 'OpaqueWebpCandidate';",
            "ck_png_audit_image_result_hashes");
        await ExpectConstraintAsync(connectionString,
            $"UPDATE web_health.png_audit_run SET status = 'Completed' WHERE id = '{runId}';",
            "ck_png_audit_run_lifecycle");
        await ExpectConstraintAsync(connectionString,
            $"UPDATE web_health.png_audit_run SET failure_code = 'MisspelledFailure' "
            + $"WHERE id = '{runId}';",
            "ck_png_audit_run_failure");
        await ExpectConstraintAsync(connectionString,
            $"UPDATE web_health.png_audit_run SET images_discovered = max_unique_images + 1 "
            + $"WHERE id = '{runId}';",
            "ck_png_audit_run_counts");
        await ExpectConstraintAsync(connectionString,
            $"UPDATE web_health.png_audit_run SET seed_url_identity_hash = decode('00', 'hex') "
            + $"WHERE id = '{runId}';",
            "ck_png_audit_run_seed_hash");
    }

    private static string ResultInsert(
        Guid runId,
        string identity,
        string classification,
        string values,
        string detectedFormat = "NULL") =>
        $"""
        INSERT INTO web_health.png_audit_image_result
            (id, run_id, image_display_url, image_identity_hash, final_display_url,
             final_identity_hash, declared_content_type, detected_format, http_status_code,
             response_bytes, width, height, frame_count, pixel_count, uses_transparency,
             transparent_pixel_count, transparent_pixel_percent, classification,
             recommendation, recorded_at)
        VALUES ('{Guid.NewGuid()}', '{runId}', 'https://invalid.example/{identity}.png',
                decode('{Hash(identity)}', 'hex'), 'https://invalid.example/{identity}.png',
                decode('{Hash("final-" + identity)}', 'hex'), 'image/png', {detectedFormat},
                {values}, '{classification}', 'None', now());
        """;

    private static async Task ExpectConstraintAsync(
        string connectionString,
        string sql,
        string constraintName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        rejected.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        rejected.ConstraintName.Should().Be(constraintName);
    }

    private static async Task VerifyColumnInventoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name, column_name
            FROM information_schema.columns
            WHERE table_schema = 'web_health' AND table_name LIKE 'png_audit_%'
            ORDER BY table_name, column_name;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);
            if (!columns.TryGetValue(table, out var names))
            {
                names = [];
                columns.Add(table, names);
            }
            names.Add(reader.GetString(1));
        }

        columns.Keys.Should().BeEquivalentTo(
            "png_audit_run", "png_audit_image_result", "png_audit_image_source",
            "png_audit_discovery_skip", "png_audit_coverage_reason");
        columns["png_audit_image_result"].Should().BeEquivalentTo(
            "id", "run_id", "image_display_url", "image_identity_hash", "final_display_url",
            "final_identity_hash", "declared_content_type", "detected_format", "http_status_code",
            "response_bytes", "width", "height", "frame_count", "pixel_count",
            "uses_transparency", "transparent_pixel_count", "transparent_pixel_percent",
            "classification", "reason_code", "recommendation", "suggested_format",
            "normalized_png_bytes", "candidate_webp_bytes", "original_savings_bytes",
            "original_savings_percent", "normalized_savings_bytes", "normalized_savings_percent",
            "recorded_at");
        columns["png_audit_image_source"].Should().BeEquivalentTo(
            "id", "image_result_id", "source_page_display_url", "source_page_identity_hash",
            "attribute_kind", "descriptor");
        columns["png_audit_discovery_skip"].Should().BeEquivalentTo(
            "id", "run_id", "source_page_display_url", "source_page_identity_hash",
            "attribute_kind", "descriptor", "bounded_safe_raw_value", "reason_code", "recorded_at");
        columns["png_audit_coverage_reason"].Should().BeEquivalentTo(
            "run_id", "area", "reason_code", "count");
        columns["png_audit_run"].Should().BeEquivalentTo(
            "id", "endpoint_id", "source", "initiated_by_user_id", "status", "failure_code",
            "safe_diagnostic", "seed_url_snapshot", "seed_url_identity_hash", "is_production_snapshot",
            "allowed_page_hosts", "allowed_page_path_prefixes", "allowed_asset_hosts",
            "query_policy", "tracking_query_parameters", "sensitive_query_parameters",
            "max_query_parameters", "max_pages", "max_depth", "max_page_bytes",
            "max_total_page_bytes", "max_image_references_per_page", "max_unique_images",
            "max_total_image_source_mappings", "max_image_bytes", "max_total_image_bytes",
            "max_width", "max_height", "max_decoded_pixels", "max_decoded_memory_bytes",
            "max_total_http_attempts", "fetch_timeout_seconds", "requests_per_second_per_host",
            "transient_retry_count", "max_duration_seconds", "min_savings_percent",
            "min_savings_bytes", "analyzer_profile", "comparison_profile", "pages_discovered",
            "images_discovered", "images_analyzed", "recommendation_count",
            "discovery_skip_count", "http_attempts", "total_page_bytes", "total_image_bytes",
            "crawl_coverage_limited", "image_analysis_coverage_limited",
            "source_mapping_coverage_limited", "attempt_count", "lease_token",
            "lease_expires_at", "queued_at", "started_at", "updated_at", "finished_at");
    }

    private static PngAuditImageIdentity Identity(string endpointUrl, string name) =>
        new(endpointUrl + "/" + name + ".png", Hash(name));

    private static string Hash(string value) => Convert.ToHexString(HashBytes(value)).ToLowerInvariant();

    private static byte[] HashBytes(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
