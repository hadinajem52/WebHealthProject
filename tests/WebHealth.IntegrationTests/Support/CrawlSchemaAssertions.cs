using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WebHealth.Application.Crawling;
using WebHealth.Infrastructure.Identity;
using WebHealth.Application.Registry;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.Persistence;
using Xunit;

namespace WebHealth.IntegrationTests.Support;

internal static class CrawlSchemaAssertions
{
    private const int PlanEvidenceRuns = 12;
    private const int PlanEvidenceLinksPerRun = 400;

    public static async Task VerifyAsync(string connectionString, Guid endpointId)
    {
        await VerifyColumnsAsync(connectionString);
        await VerifyRunStatusContractAsync(connectionString, endpointId);
        await VerifyOverrideContractAsync(connectionString, endpointId);
        await VerifySourceTargetUniquenessAsync(connectionString, endpointId);
        await VerifyBoundedBatchPersistenceAsync(connectionString, endpointId);
        await VerifyOneActiveRunPerEndpointAsync(connectionString, endpointId);
        await VerifyResultsCascadeWithTheirRunAsync(connectionString, endpointId);
        await VerifyReportingIndexServesTheFilterAsync(connectionString, endpointId);
        await VerifyAbandonedRunsAreRetiredAsync(connectionString, endpointId);
        await VerifyExecutionClaimFencesARetiredRunAsync(connectionString, endpointId);
    }

    private static async Task VerifyBoundedBatchPersistenceAsync(
        string connectionString,
        Guid endpointId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging()
            .AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var sink = scope.ServiceProvider.GetRequiredService<ICrawlResultSink>();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var runId = Guid.NewGuid();
        await sink.BeginRunAsync(new(
            runId,
            endpointId,
            ["https://batch.test/?access_token=seed-secret"],
            new([], [], "Canonicalize", 1000, 5, false),
            TimeProvider.System.GetUtcNow()));
        var records = Enumerable.Range(0, 501)
            .Select(index => new CrawlLinkRecord(
                runId,
                "https://batch.test/source",
                index < 2
                    ? "https://batch.test/target?token=REDACTED"
                    : $"https://batch.test/target/{index}",
                true,
                1,
                CrawlLinkClassifications.Broken,
                404,
                0,
                null,
                null,
                8)
            {
                TargetUrlIdentity = index < 2
                    ? $"https://batch.test/target?token=secret-{index}"
                    : null
            })
            .ToArray();

        (await sink.RecordLinksAsync(records)).Should().Be(501);

        var additional = new CrawlLinkRecord(
            runId,
            "https://batch.test/source",
            "https://batch.test/target/501",
            true,
            1,
            CrawlLinkClassifications.Broken,
            404,
            0,
            null,
            null,
            8);
        (await sink.RecordLinksAsync([records[0], additional])).Should().Be(502);

        var stored = await database.CrawlLinkResults.AsNoTracking()
            .Where(result => result.RunId == runId)
            .ToArrayAsync();
        stored.Should().HaveCount(502);
        stored.Should().OnlyContain(result => !result.TargetUrl.Contains("secret-", StringComparison.Ordinal));
        stored.Count(result => result.TargetUrl.EndsWith("token=REDACTED", StringComparison.Ordinal))
            .Should().Be(2);
        (await database.CrawlRuns.AsNoTracking().SingleAsync(run => run.Id == runId))
            .SeedUrls.Should().Be("https://batch.test/?access_token=REDACTED");

        await DeleteRunsAsync(connectionString, runId);
    }

    private static async Task VerifyExecutionClaimFencesARetiredRunAsync(
        string connectionString,
        Guid endpointId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging()
            .AddInfrastructure(configuration).BuildServiceProvider();

        var runId = await InsertRunningRunAsync(connectionString, endpointId, TimeSpan.FromHours(6));
        await using var scope = services.CreateAsyncScope();
        var sink = scope.ServiceProvider.GetRequiredService<ICrawlResultSink>();

        var owner = Guid.NewGuid();
        (await sink.TryClaimRunAsync(runId, owner)).Should().BeTrue(
            "the first delivery of a job finds the run unclaimed");
        (await sink.TryClaimRunAsync(runId, Guid.NewGuid())).Should().BeFalse(
            "a redelivered job must not crawl a site the first delivery is already crawling");

        await RetireAbandonedRunsAsync(services);

        (await sink.RecordRunOutcomeAsync(new(
            runId, CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted,
            9, 9, false, CrawlOverrideRefusals.NotRequested, []), owner)).Should().BeFalse(
            "a worker that outlived the sweep must not reopen the run it closed");

        var retired = await ReadRunAsync(connectionString, runId);
        retired.Status.Should().Be(CrawlRunStatuses.Failed,
            "the retirement stands: the late outcome was dropped, not applied");
        retired.FailureReason.Should().NotBeNullOrWhiteSpace();

        (await sink.TryClaimRunAsync(runId, Guid.NewGuid())).Should().BeFalse(
            "a run that is no longer in flight is nobody's to perform");

        await DeleteRunsAsync(connectionString, runId);
    }

    private static async Task VerifyAbandonedRunsAreRetiredAsync(
        string connectionString,
        Guid endpointId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging()
            .AddInfrastructure(configuration).BuildServiceProvider();

        var abandoned = await InsertRunningRunAsync(connectionString, endpointId, TimeSpan.FromHours(6));
        await RetireAbandonedRunsAsync(services);
        var retired = await ReadRunAsync(connectionString, abandoned);

        retired.Status.Should().Be(CrawlRunStatuses.Failed,
            "a run whose process is gone must stop reporting itself as in flight");
        retired.StopReason.Should().Be(CrawlStopReasons.Failed);
        retired.FinishedAt.Should().NotBeNull(
            "ck_crawl_run_finished_when_terminal pairs a terminal status with a finish time");
        retired.FailureReason.Should().NotBeNullOrWhiteSpace(
            "the reader is owed why the run was closed without an outcome of its own");

        var live = await InsertRunningRunAsync(connectionString, endpointId, TimeSpan.Zero);
        await RetireAbandonedRunsAsync(services);
        (await ReadRunAsync(connectionString, live)).Status.Should().Be(CrawlRunStatuses.Running,
            "a crawl that has only just started is slow, not abandoned");

        await DeleteRunsAsync(connectionString, abandoned, live);
    }

    private static async Task RetireAbandonedRunsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICrawlReconciler>().RetireAbandonedRunsAsync();
    }

    private static async Task<(string Status, string StopReason, DateTimeOffset? FinishedAt, string? FailureReason)>
        ReadRunAsync(string connectionString, Guid runId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT status, stop_reason, finished_at, failure_reason
            FROM web_health.crawl_run WHERE id = @id;
            """, connection);
        command.Parameters.AddWithValue("id", runId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue($"crawl run {runId} should still exist");
        return (
            reader.GetString(0),
            reader.GetString(1),
            await reader.IsDBNullAsync(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            await reader.IsDBNullAsync(3) ? null : reader.GetString(3));
    }

    private static async Task DeleteRunsAsync(string connectionString, params Guid[] runIds)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM web_health.crawl_run WHERE id = ANY(@ids);", connection);
        command.Parameters.AddWithValue("ids", runIds);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task VerifyColumnsAsync(string connectionString)
    {
        (await ColumnsOfAsync(connectionString, "crawl_run")).Should().BeEquivalentTo(
            "id", "endpoint_id", "status", "stop_reason", "seed_urls", "pages_fetched",
            "links_recorded", "robots_override_granted", "robots_override_refused_because",
            "allowed_hosts", "allowed_path_prefixes", "query_policy", "max_pages", "max_depth",
            "check_external_links", "failure_reason", "coverage_limited", "started_at",
            "finished_at", "execution_claim_id");

        (await ColumnsOfAsync(connectionString, "crawl_link_result")).Should().BeEquivalentTo(
            "id", "run_id", "source_url", "source_url_hash", "target_url", "target_url_hash",
            "classification", "skip_reason", "status_code", "redirect_count", "final_url",
            "is_internal", "depth", "duration_ms", "recorded_at");
    }

    private static async Task VerifyRunStatusContractAsync(string connectionString, Guid endpointId)
    {
        await RunInsertRejectedAsync(connectionString, endpointId,
            CrawlRunStatuses.Completed, CrawlStopReasons.Cancelled,
            "ck_crawl_run_status_stop_reason");
        await RunInsertRejectedAsync(connectionString, endpointId,
            CrawlRunStatuses.Cancelled, CrawlStopReasons.FrontierExhausted,
            "ck_crawl_run_status_stop_reason");
        await RunInsertRejectedAsync(connectionString, endpointId,
            "Finished", CrawlStopReasons.FrontierExhausted,
            "ck_crawl_run_status");

        await RunInsertRejectedAsync(connectionString, endpointId,
            CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted,
            "ck_crawl_run_finished_when_terminal", finished: false);
    }

    private static async Task VerifyOverrideContractAsync(string connectionString, Guid endpointId)
    {
        await RunInsertRejectedAsync(connectionString, endpointId,
            CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted,
            "ck_crawl_run_override", overrideGranted: true, refusedBecause: "ProductionTarget");
        await RunInsertRejectedAsync(connectionString, endpointId,
            CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted,
            "ck_crawl_run_override", overrideGranted: false, refusedBecause: null);
    }

    private static async Task VerifySourceTargetUniquenessAsync(string connectionString, Guid endpointId)
    {
        var runId = await InsertRunAsync(connectionString, endpointId);
        await InsertLinkAsync(connectionString, runId, "https://pairs.test/a", "https://pairs.test/gone");

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertLinkAsync(connectionString, runId, "https://pairs.test/a", "https://pairs.test/gone"));
        duplicate.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        duplicate.ConstraintName.Should().Be("ux_crawl_link_result_pair");

        await InsertLinkAsync(connectionString, runId, "https://pairs.test/b", "https://pairs.test/gone");

        await InsertLinkAsync(connectionString, runId, null, "https://pairs.test/");
        var duplicateSeed = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertLinkAsync(connectionString, runId, null, "https://pairs.test/"));
        duplicateSeed.ConstraintName.Should().Be("ux_crawl_link_result_pair",
            "NULLS NOT DISTINCT is what stops one seed being stored twice");

        var otherRun = await InsertRunAsync(connectionString, endpointId);
        await InsertLinkAsync(connectionString, otherRun, "https://pairs.test/a", "https://pairs.test/gone");
    }

    private static async Task VerifyOneActiveRunPerEndpointAsync(string connectionString, Guid endpointId)
    {
        var first = await InsertRunningRunAsync(connectionString, endpointId);

        var second = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertRunningRunAsync(connectionString, endpointId));
        second.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        second.ConstraintName.Should().Be("ux_crawl_run_active");

        await CloseRunAsync(connectionString, first);
        var third = await InsertRunningRunAsync(connectionString, endpointId);
        await CloseRunAsync(connectionString, third);
    }

    private static async Task VerifyResultsCascadeWithTheirRunAsync(string connectionString, Guid endpointId)
    {
        var runId = await InsertRunAsync(connectionString, endpointId);
        await InsertLinkAsync(connectionString, runId, "https://cascade.test/a", "https://cascade.test/gone");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var delete = new NpgsqlCommand(
            "DELETE FROM web_health.crawl_run WHERE id = @id;", connection))
        {
            delete.Parameters.AddWithValue("id", runId);
            (await delete.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM web_health.crawl_link_result WHERE run_id = @id;", connection);
        count.Parameters.AddWithValue("id", runId);
        (await count.ExecuteScalarAsync()).Should().Be(0L);
    }

    private static async Task VerifyReportingIndexServesTheFilterAsync(
        string connectionString,
        Guid endpointId)
    {
        var runId = await SeedPlanEvidenceAsync(connectionString, endpointId);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var analyze = new NpgsqlCommand("ANALYZE web_health.crawl_link_result;", connection))
        {
            await analyze.ExecuteNonQueryAsync();
        }

        var plan = await ExplainAsync(connection,
            """
            SELECT source_url, target_url, status_code
            FROM web_health.crawl_link_result
            WHERE run_id = @run AND classification = 'Broken';
            """,
            runId);

        plan.Should().Contain("ix_crawl_link_result_run_classification",
            $"the broken-link filter must be index-served. Plan was:\n{plan}");
        plan.Should().NotContain("Seq Scan on crawl_link_result",
            $"a sequential scan here is the Phase 5 shape repeating. Plan was:\n{plan}");
    }

    private static async Task<Guid> SeedPlanEvidenceAsync(string connectionString, Guid endpointId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var runId = Guid.Empty;
        for (var run = 0; run < PlanEvidenceRuns; run++)
        {
            runId = await InsertRunAsync(connectionString, endpointId, seedPrefix: "https://plan.test");

            await using var command = new NpgsqlCommand(
                """
                INSERT INTO web_health.crawl_link_result
                    (id, run_id, source_url, source_url_hash, target_url, target_url_hash,
                     classification, skip_reason, status_code, redirect_count, final_url,
                     is_internal, depth, duration_ms, recorded_at)
                SELECT
                    gen_random_uuid(), @run,
                    'https://plan.test/source-' || i,
                    sha256(('https://plan.test/source-' || i)::bytea),
                    'https://plan.test/target-' || i,
                    sha256(('https://plan.test/target-' || i)::bytea),
                    CASE WHEN i % 20 = 0 THEN 'Broken' ELSE 'Healthy' END,
                    NULL,
                    CASE WHEN i % 20 = 0 THEN 404 ELSE 200 END,
                    0, NULL, true, 1, 10, now()
                FROM generate_series(1, @count) AS i;
                """, connection);
            command.Parameters.AddWithValue("run", runId);
            command.Parameters.AddWithValue("count", PlanEvidenceLinksPerRun);
            await command.ExecuteNonQueryAsync();
        }

        return runId;
    }

    private static async Task<string> ExplainAsync(NpgsqlConnection connection, string sql, Guid runId)
    {
        await using var command = new NpgsqlCommand($"EXPLAIN (COSTS OFF) {sql}", connection);
        command.Parameters.AddWithValue("run", runId);
        await using var reader = await command.ExecuteReaderAsync();

        var lines = new List<string>();
        while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        return string.Join('\n', lines);
    }

    private static async Task<IReadOnlyList<string>> ColumnsOfAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'web_health' AND table_name = @table
            ORDER BY column_name;
            """, connection);
        command.Parameters.AddWithValue("table", table);

        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        return columns;
    }

    private static async Task<Guid> InsertRunAsync(
        string connectionString,
        Guid endpointId,
        string seedPrefix = "https://pairs.test")
    {
        var runId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO web_health.crawl_run
                (id, endpoint_id, status, stop_reason, seed_urls, pages_fetched, links_recorded,
                 robots_override_granted, robots_override_refused_because, query_policy,
                 max_pages, max_depth, check_external_links, started_at, finished_at)
            VALUES (@id, @endpoint, 'Completed', 'FrontierExhausted', @seeds, 1, 1,
                    false, 'NotRequested', 'Canonicalize', 1000, 5, false, now(), now());
            """, connection);
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("endpoint", endpointId);
        command.Parameters.AddWithValue("seeds", $"{seedPrefix}/");
        await command.ExecuteNonQueryAsync();
        return runId;
    }

    private static async Task<Guid> InsertRunningRunAsync(
        string connectionString,
        Guid endpointId,
        TimeSpan startedAgo = default)
    {
        var runId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO web_health.crawl_run
                (id, endpoint_id, status, stop_reason, seed_urls, pages_fetched, links_recorded,
                 robots_override_granted, robots_override_refused_because, query_policy,
                 max_pages, max_depth, check_external_links, started_at, finished_at)
            VALUES (@id, @endpoint, 'Running', 'FrontierExhausted', @seeds, 0, 0,
                    false, 'NotRequested', 'Canonicalize', 1000, 5, false, now() - @startedAgo, NULL);
            """, connection);
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("endpoint", endpointId);
        command.Parameters.AddWithValue("seeds", "https://active.test/");
        command.Parameters.AddWithValue("startedAgo", startedAgo);
        await command.ExecuteNonQueryAsync();
        return runId;
    }

    private static async Task CloseRunAsync(string connectionString, Guid runId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            UPDATE web_health.crawl_run
            SET status = 'Completed', stop_reason = 'FrontierExhausted', finished_at = now()
            WHERE id = @id;
            """, connection);
        command.Parameters.AddWithValue("id", runId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertLinkAsync(
        string connectionString,
        Guid runId,
        string? sourceUrl,
        string targetUrl)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO web_health.crawl_link_result
                (id, run_id, source_url, source_url_hash, target_url, target_url_hash,
                 classification, skip_reason, status_code, redirect_count, final_url,
                 is_internal, depth, duration_ms, recorded_at)
            VALUES (@id, @run, @source, @source_hash, @target, @target_hash,
                    'Broken', NULL, 404, 0, NULL, true, 1, 12, now());
            """, connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("run", runId);
        command.Parameters.AddWithValue("source", (object?)sourceUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("source_hash",
            sourceUrl is null ? DBNull.Value : CrawlResultSink.Hash(sourceUrl));
        command.Parameters.AddWithValue("target", targetUrl);
        command.Parameters.AddWithValue("target_hash", CrawlResultSink.Hash(targetUrl));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RunInsertRejectedAsync(
        string connectionString,
        Guid endpointId,
        string status,
        string stopReason,
        string expectedConstraint,
        bool finished = true,
        bool overrideGranted = false,
        string? refusedBecause = "NotRequested")
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO web_health.crawl_run
                (id, endpoint_id, status, stop_reason, seed_urls, pages_fetched, links_recorded,
                 robots_override_granted, robots_override_refused_because, query_policy,
                 max_pages, max_depth, check_external_links, started_at, finished_at)
            VALUES (@id, @endpoint, @status, @stop_reason, 'https://rejected.test/', 0, 0,
                    @granted, @refused, 'Canonicalize', 1000, 5, false, now(),
                    CASE WHEN @finished THEN now() END);
            """, connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("endpoint", endpointId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("stop_reason", stopReason);
        command.Parameters.AddWithValue("granted", overrideGranted);
        command.Parameters.AddWithValue("refused", (object?)refusedBecause ?? DBNull.Value);
        command.Parameters.AddWithValue("finished", finished);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.Should().Be(expectedConstraint);
    }

    public static async Task VerifyComparisonAsync(string connectionString, Guid endpointId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging()
            .AddInfrastructure(configuration).BuildServiceProvider();

        var previousRun = Guid.CreateVersion7();
        var currentRun = Guid.CreateVersion7();
        await WriteRunAsync(services, endpointId, previousRun,
            ("https://cmp.test/a", "https://cmp.test/fixed", CrawlLinkClassifications.Broken),
            ("https://cmp.test/a", "https://cmp.test/still", CrawlLinkClassifications.Broken));
        await WriteRunAsync(services, endpointId, currentRun,
            ("https://cmp.test/a", "https://cmp.test/fixed", CrawlLinkClassifications.Healthy),
            ("https://cmp.test/a", "https://cmp.test/still", CrawlLinkClassifications.Broken),
            ("https://cmp.test/a", "https://cmp.test/new", CrawlLinkClassifications.Broken));
        await WriteSkipsAsync(services, currentRun);

        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICrawlReportReader>();
        var access = await AdministratorAccessAsync(scope);
        var comparison = await reader.CompareLatestAsync(endpointId, access);

        comparison.CurrentRunId.Should().Be(currentRun);
        comparison.PreviousRunId.Should().Be(previousRun);
        comparison.New.Sample.Select(link => link.TargetUrl).Should().Equal("https://cmp.test/new");
        comparison.New.TotalCount.Should().Be(1);
        comparison.Continuing.Sample.Select(link => link.TargetUrl).Should().Equal("https://cmp.test/still");
        comparison.Resolved.Sample.Select(link => link.TargetUrl).Should().Equal("https://cmp.test/fixed");

        comparison.Indeterminate.TotalCount.Should().Be(0, "every previously broken link was re-checked");

        var broken = await reader.ListBrokenLinksAsync(currentRun, limit: 100, access);
        broken.Should().HaveCount(2, "a healthy link is not a broken-link report row");
        broken.Should().OnlyContain(link => link.SourceUrl == "https://cmp.test/a");

        var skips = await reader.ListSkipReasonsAsync(currentRun, access);
        skips.Should().Equal(
            new CrawlSkipSummary(CrawlSkipReasons.RobotsDisallowed, 2),
            new CrawlSkipSummary(CrawlSkipReasons.ExternalCheckDisabled, 1));

        var runs = await reader.ListRunsAsync(endpointId, 2, access);
        runs.Should().HaveCount(2);
        runs[0].RunId.Should().Be(currentRun);
        runs[0].BrokenLinkCount.Should().Be(2);
        runs[0].CoveredWholeScope.Should().BeTrue();

        await VerifyRunsAreInvisibleWithoutAccessAsync(services, endpointId, currentRun);
        await VerifyComparisonIsBoundedAsync(services, endpointId);
        await VerifyPartialRunIsNeverABaselineAsync(services, endpointId);
        await VerifyRunThatFetchedNothingIsNeverABaselineAsync(services, endpointId);
        await VerifyCoverageLimitedRunIsNeverABaselineAsync(services, endpointId);
        await VerifyUncheckedLinkIsNotReportedResolvedAsync(services, endpointId);
        await VerifyRedactedUrlsUseHashesForComparisonAsync(services, endpointId);
        await VerifyRunStartIsReplayableAsync(services, endpointId);
    }

    private static async Task VerifyComparisonIsBoundedAsync(IServiceProvider services, Guid endpointId)
    {
        var oversize = CrawlReportReader.ComparisonSampleSize + 12;
        var previousRun = Guid.CreateVersion7();
        var currentRun = Guid.CreateVersion7();

        await WriteRunAsync(services, endpointId, previousRun, CrawlStopReasons.FrontierExhausted,
            [.. Enumerable.Range(0, oversize).Select(index =>
                ("https://bulk.test/source", $"https://bulk.test/target-{index}",
                    CrawlLinkClassifications.Healthy))]);
        await WriteRunAsync(services, endpointId, currentRun, CrawlStopReasons.FrontierExhausted,
            [.. Enumerable.Range(0, oversize).Select(index =>
                ("https://bulk.test/source", $"https://bulk.test/target-{index}",
                    CrawlLinkClassifications.Broken))]);

        await using var scope = services.CreateAsyncScope();
        var comparison = await scope.ServiceProvider.GetRequiredService<ICrawlReportReader>()
            .CompareLatestAsync(endpointId, await AdministratorAccessAsync(scope));

        comparison.CurrentRunId.Should().Be(currentRun);
        comparison.New.TotalCount.Should().Be(oversize, "the count is exact and computed in the database");
        comparison.New.Sample.Should().HaveCount(CrawlReportReader.ComparisonSampleSize,
            "only what is rendered is capped");
        comparison.New.HasMore.Should().BeTrue();
    }

    private static async Task VerifyRunsAreInvisibleWithoutAccessAsync(
        IServiceProvider services,
        Guid endpointId,
        Guid knownRunId)
    {
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICrawlReportReader>();

        var strangerAccess = new RegistryAccessContext(Guid.CreateVersion7(), [ApplicationRoles.Viewer]);

        (await reader.ListRunsAsync(endpointId, 10, strangerAccess)).Should().BeEmpty(
            "an endpoint id is a parameter, not a permission");
        (await reader.FindRunAsync(knownRunId, strangerAccess)).Should().BeNull();
        (await reader.ListBrokenLinksAsync(knownRunId, 100, strangerAccess)).Should().BeEmpty();
        (await reader.CompareLatestAsync(endpointId, strangerAccess)).CurrentRunId.Should().BeNull();

        var administrator = await AdministratorAccessAsync(scope);
        (await reader.ListRunsAsync(endpointId, 10, administrator)).Should().NotBeEmpty();
        (await reader.FindRunAsync(knownRunId, administrator)).Should().NotBeNull();
    }

    private static async Task VerifyPartialRunIsNeverABaselineAsync(
        IServiceProvider services,
        Guid endpointId)
    {
        var fullScopeRun = Guid.CreateVersion7();
        await WriteRunAsync(services, endpointId, fullScopeRun,
            ("https://partial.test/a", "https://partial.test/gone", CrawlLinkClassifications.Broken));

        var partialRun = Guid.CreateVersion7();
        await WriteRunAsync(services, endpointId, partialRun, CrawlStopReasons.PageLimit);

        await using var scope = services.CreateAsyncScope();
        var comparison = await scope.ServiceProvider.GetRequiredService<ICrawlReportReader>()
            .CompareLatestAsync(endpointId, await AdministratorAccessAsync(scope));

        comparison.CurrentRunId.Should().Be(fullScopeRun,
            "a page-limited run must not displace the last full-scope run as the current side");
    }

    private static async Task VerifyRunThatFetchedNothingIsNeverABaselineAsync(
        IServiceProvider services,
        Guid endpointId)
    {
        var fullScopeRun = Guid.CreateVersion7();
        await WriteRunAsync(services, endpointId, fullScopeRun, CrawlStopReasons.FrontierExhausted,
            ("https://blocked.test/a", "https://blocked.test/broken", CrawlLinkClassifications.Broken));

        var refusedRun = Guid.CreateVersion7();
        await using (var writing = services.CreateAsyncScope())
        {
            var sink = writing.ServiceProvider.GetRequiredService<ICrawlResultSink>();
            await sink.BeginRunAsync(new(
                refusedRun, endpointId, ["https://blocked.test/"],
                new([], [], "Canonicalize", 1000, 5, false), DateTimeOffset.UtcNow));

            await sink.RecordLinkAsync(new(
                refusedRun, null, "https://blocked.test/", true, 0,
                CrawlLinkClassifications.Skipped, null, 0, null,
                CrawlSkipReasons.RobotsDisallowed, null));

            await FinishRunAsync(sink, refusedRun, new(
                refusedRun, CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted,
                0, 1, false, CrawlOverrideRefusals.NotRequested, []));
        }

        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICrawlReportReader>();
        var access = await AdministratorAccessAsync(scope);

        var refused = await reader.FindRunAsync(refusedRun, access);
        refused!.CoveredWholeScope.Should().BeFalse(
            "a run that fetched no pages examined nothing, whatever its stop reason");

        var comparison = await reader.CompareLatestAsync(endpointId, access);
        comparison.CurrentRunId.Should().Be(fullScopeRun,
            "a run that fetched nothing must not displace the last run that actually looked");
        comparison.Resolved.Sample.Should().NotContain(
            link => link.TargetUrl == "https://blocked.test/broken",
            "a link is only resolved when a crawl re-checked it, not when a crawl was refused");
    }

    private static async Task VerifyCoverageLimitedRunIsNeverABaselineAsync(
        IServiceProvider services,
        Guid endpointId)
    {
        var fullScopeRun = Guid.CreateVersion7();
        await WriteRunAsync(services, endpointId, fullScopeRun, CrawlStopReasons.FrontierExhausted,
            ("https://partial.test/a", "https://partial.test/broken", CrawlLinkClassifications.Broken));

        var limitedRun = Guid.CreateVersion7();
        await using (var writing = services.CreateAsyncScope())
        {
            var sink = writing.ServiceProvider.GetRequiredService<ICrawlResultSink>();
            await sink.BeginRunAsync(new(
                limitedRun, endpointId, ["https://partial.test/"],
                new([], [], "Canonicalize", 1000, 5, false), DateTimeOffset.UtcNow));

            await sink.RecordLinkAsync(new(
                limitedRun, "https://partial.test/a", "https://partial.test/ok", true, 1,
                CrawlLinkClassifications.Healthy, 200, 0, null, null, 8));

            await FinishRunAsync(sink, limitedRun, new(
                limitedRun, CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted,
                4, 1, false, CrawlOverrideRefusals.NotRequested, [])
            {
                CoverageLimited = true
            });
        }

        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICrawlReportReader>();
        var access = await AdministratorAccessAsync(scope);

        var limited = await reader.FindRunAsync(limitedRun, access);
        limited!.CoverageLimited.Should().BeTrue("the flag has to survive the round trip");
        limited.CoveredWholeScope.Should().BeFalse(
            "part of the site went unexamined, whatever the stop reason and page count say");

        var comparison = await reader.CompareLatestAsync(endpointId, access);
        comparison.CurrentRunId.Should().Be(fullScopeRun,
            "a run that could not read part of the site must not displace the last full-scope run");
        comparison.Resolved.Sample.Should().NotContain(
            link => link.TargetUrl == "https://partial.test/broken",
            "the pair is absent because nobody read the page that carries it, which is not evidence "
            + "that it was fixed");
    }

    private static async Task VerifyUncheckedLinkIsNotReportedResolvedAsync(
        IServiceProvider services,
        Guid endpointId)
    {
        var previousRun = Guid.CreateVersion7();
        var currentRun = Guid.CreateVersion7();
        await WriteRunAsync(services, endpointId, previousRun, CrawlStopReasons.FrontierExhausted,
            ("https://cmp.test/b", "https://cmp.test/slow", CrawlLinkClassifications.Broken));
        await WriteRunAsync(services, endpointId, currentRun, CrawlStopReasons.FrontierExhausted,
            ("https://cmp.test/b", "https://cmp.test/slow", CrawlLinkClassifications.Timeout));

        await using var scope = services.CreateAsyncScope();
        var comparison = await scope.ServiceProvider.GetRequiredService<ICrawlReportReader>()
            .CompareLatestAsync(endpointId, await AdministratorAccessAsync(scope));

        comparison.Resolved.Sample.Should().NotContain(link => link.TargetUrl == "https://cmp.test/slow",
            "a timeout is not evidence that a broken link was fixed");
        comparison.Indeterminate.Sample.Select(link => link.TargetUrl).Should()
            .Contain("https://cmp.test/slow");
    }

    private static async Task VerifyRedactedUrlsUseHashesForComparisonAsync(
        IServiceProvider services,
        Guid endpointId)
    {
        var previousRun = Guid.CreateVersion7();
        var currentRun = Guid.CreateVersion7();
        await using (var writing = services.CreateAsyncScope())
        {
            var sink = writing.ServiceProvider.GetRequiredService<ICrawlResultSink>();
            await sink.BeginRunAsync(new(
                previousRun, endpointId, ["https://identity.test/"],
                new([], [], "Canonicalize", 1000, 5, false), DateTimeOffset.UtcNow));
            await sink.RecordLinkAsync(new(
                previousRun,
                "https://identity.test/source",
                "https://identity.test/reset?token=REDACTED",
                true,
                1,
                CrawlLinkClassifications.Broken,
                404,
                0,
                null,
                null,
                8)
            {
                TargetUrlIdentity = "https://identity.test/reset?token=old"
            });
            await FinishRunAsync(sink, previousRun, new(
                previousRun, CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted,
                1, 1, false, CrawlOverrideRefusals.NotRequested, []));

            await sink.BeginRunAsync(new(
                currentRun, endpointId, ["https://identity.test/"],
                new([], [], "Canonicalize", 1000, 5, false), DateTimeOffset.UtcNow));
            await sink.RecordLinkAsync(new(
                currentRun,
                "https://identity.test/source",
                "https://identity.test/reset?token=REDACTED",
                true,
                1,
                CrawlLinkClassifications.Broken,
                404,
                0,
                null,
                null,
                8)
            {
                TargetUrlIdentity = "https://identity.test/reset?token=new"
            });
            await FinishRunAsync(sink, currentRun, new(
                currentRun, CrawlRunStatuses.Completed, CrawlStopReasons.FrontierExhausted,
                1, 1, false, CrawlOverrideRefusals.NotRequested, []));
        }

        await using var reading = services.CreateAsyncScope();
        var reader = reading.ServiceProvider.GetRequiredService<ICrawlReportReader>();
        var comparison = await reader.CompareLatestAsync(
            endpointId, await AdministratorAccessAsync(reading));
        comparison.New.Sample.Should().Contain(link =>
            link.TargetUrl == "https://identity.test/reset?token=REDACTED");
        comparison.Continuing.Sample.Should().NotContain(link =>
            link.TargetUrl == "https://identity.test/reset?token=REDACTED");
    }

    private static async Task VerifyRunStartIsReplayableAsync(IServiceProvider services, Guid endpointId)
    {
        var runId = Guid.CreateVersion7();
        var start = new CrawlRunStart(
            runId, endpointId, ["https://replay.test/"],
            new([], [], "Canonicalize", 1000, 5, false), DateTimeOffset.UtcNow);

        await using (var first = services.CreateAsyncScope())
        {
            await first.ServiceProvider.GetRequiredService<ICrawlResultSink>().BeginRunAsync(start);
        }

        await using var second = services.CreateAsyncScope();
        var sink = second.ServiceProvider.GetRequiredService<ICrawlResultSink>();
        await sink.Invoking(item => item.BeginRunAsync(start)).Should().NotThrowAsync(
            "a replayed start must not fail the one operation that cannot be retried");
    }

    private static async Task<RegistryAccessContext> AdministratorAccessAsync(AsyncServiceScope scope)
    {
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var administrator = await database.Users
            .SingleAsync(user => user.Email == "bootstrap@example.test");
        return new(administrator.Id, [ApplicationRoles.Administrator]);
    }

    private static Task WriteRunAsync(
        IServiceProvider services,
        Guid endpointId,
        Guid runId,
        params (string Source, string Target, string Classification)[] links) =>
        WriteRunAsync(services, endpointId, runId, CrawlStopReasons.FrontierExhausted, links);

    private static async Task WriteSkipsAsync(IServiceProvider services, Guid runId)
    {
        await using var scope = services.CreateAsyncScope();
        var sink = scope.ServiceProvider.GetRequiredService<ICrawlResultSink>();
        await sink.RecordLinkAsync(new(
            runId, "https://cmp.test/a", "https://cmp.test/blocked-1", true, 1,
            CrawlLinkClassifications.Skipped, null, 0, null, CrawlSkipReasons.RobotsDisallowed, null));
        await sink.RecordLinkAsync(new(
            runId, "https://cmp.test/a", "https://cmp.test/blocked-2", true, 1,
            CrawlLinkClassifications.Skipped, null, 0, null, CrawlSkipReasons.RobotsDisallowed, null));
        await sink.RecordLinkAsync(new(
            runId, "https://cmp.test/a", "https://external.test/blocked", false, 1,
            CrawlLinkClassifications.Skipped, null, 0, null, CrawlSkipReasons.ExternalCheckDisabled, null));
    }

    private static async Task WriteRunAsync(
        IServiceProvider services,
        Guid endpointId,
        Guid runId,
        string stopReason,
        params (string Source, string Target, string Classification)[] links)
    {
        await using var scope = services.CreateAsyncScope();
        var sink = scope.ServiceProvider.GetRequiredService<ICrawlResultSink>();
        await sink.BeginRunAsync(new(
            runId, endpointId, ["https://cmp.test/"],
            new([], [], "Canonicalize", 1000, 5, false), DateTimeOffset.UtcNow));

        foreach (var (source, target, classification) in links)
        {
            await sink.RecordLinkAsync(new(
                runId, source, target, true, 1, classification, null, 0, null, null, 8));
        }

        await FinishRunAsync(sink, runId, new(
            runId, CrawlRunStatuses.Completed, stopReason,
            links.Length, links.Length, false, CrawlOverrideRefusals.NotRequested, []));
    }

    private static async Task FinishRunAsync(
        ICrawlResultSink sink,
        Guid runId,
        CrawlRunOutcome outcome)
    {
        var executionClaimId = Guid.NewGuid();
        (await sink.TryClaimRunAsync(runId, executionClaimId)).Should().BeTrue(
            "the run was opened by this test and nothing else can have claimed it");
        (await sink.RecordRunOutcomeAsync(outcome, executionClaimId)).Should().BeTrue(
            "a run this execution claimed and nobody closed accepts its outcome");
    }
}
