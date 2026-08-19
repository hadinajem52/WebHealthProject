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

/// <summary>
/// Phase 6 increment 6.7. The crawl schema's contract, the source-target uniqueness BR-L07 depends
/// on, the status/stop-reason pairing that keeps BR-L10 true in the database rather than by
/// convention, and the plan evidence that the reporting index is actually the one PostgreSQL uses.
/// </summary>
internal static class CrawlSchemaAssertions
{
    /// <summary>
    /// Enough rows that a sequential scan is not simply the cheapest plan. A plan assertion against
    /// a table of ten rows proves nothing: PostgreSQL would scan it regardless of any index.
    /// </summary>
    private const int PlanEvidenceRuns = 12;
    private const int PlanEvidenceLinksPerRun = 400;

    public static async Task VerifyAsync(string connectionString, Guid endpointId)
    {
        await VerifyColumnsAsync(connectionString);
        await VerifyRunStatusContractAsync(connectionString, endpointId);
        await VerifyOverrideContractAsync(connectionString, endpointId);
        await VerifySourceTargetUniquenessAsync(connectionString, endpointId);
        await VerifyResultsCascadeWithTheirRunAsync(connectionString, endpointId);
        await VerifyReportingIndexServesTheFilterAsync(connectionString, endpointId);
    }

    private static async Task VerifyColumnsAsync(string connectionString)
    {
        (await ColumnsOfAsync(connectionString, "crawl_run")).Should().BeEquivalentTo(
            "id", "endpoint_id", "status", "stop_reason", "seed_urls", "pages_fetched",
            "links_recorded", "robots_override_granted", "robots_override_refused_because",
            "allowed_hosts", "allowed_path_prefixes", "query_policy", "max_pages", "max_depth",
            "check_external_links", "failure_reason", "started_at", "finished_at");

        (await ColumnsOfAsync(connectionString, "crawl_link_result")).Should().BeEquivalentTo(
            "id", "run_id", "source_url", "source_url_hash", "target_url", "target_url_hash",
            "classification", "skip_reason", "status_code", "redirect_count", "final_url",
            "is_internal", "depth", "duration_ms", "recorded_at");
    }

    /// <summary>
    /// BR-L10 in the database. A cancelled run can never be stored as complete, whatever a future
    /// caller does — a partial crawl reported as a clean completed run is worse than no crawl.
    /// </summary>
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

        // A terminal run carries a finish time and a running one does not, so an interrupted
        // process cannot leave a run that reads as ended.
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

    /// <summary>
    /// BR-L07. One result per source-target pair per run, enforced by the index rather than by the
    /// writer. The seed case is the one worth spelling out: its source is null, and a default
    /// unique index treats every null as distinct, which would let one seed be stored repeatedly.
    /// </summary>
    private static async Task VerifySourceTargetUniquenessAsync(string connectionString, Guid endpointId)
    {
        var runId = await InsertRunAsync(connectionString, endpointId);
        await InsertLinkAsync(connectionString, runId, "https://pairs.test/a", "https://pairs.test/gone");

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertLinkAsync(connectionString, runId, "https://pairs.test/a", "https://pairs.test/gone"));
        duplicate.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        duplicate.ConstraintName.Should().Be("ux_crawl_link_result_pair");

        // A different source pointing at the same target is a different pair and is allowed: that
        // is what makes "which pages contain this broken link" answerable.
        await InsertLinkAsync(connectionString, runId, "https://pairs.test/b", "https://pairs.test/gone");

        await InsertLinkAsync(connectionString, runId, null, "https://pairs.test/");
        var duplicateSeed = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertLinkAsync(connectionString, runId, null, "https://pairs.test/"));
        duplicateSeed.ConstraintName.Should().Be("ux_crawl_link_result_pair",
            "NULLS NOT DISTINCT is what stops one seed being stored twice");

        // The same pair in a different run is a different row: runs are compared, not merged.
        var otherRun = await InsertRunAsync(connectionString, endpointId);
        await InsertLinkAsync(connectionString, otherRun, "https://pairs.test/a", "https://pairs.test/gone");
    }

    /// <summary>
    /// A result can never outlive the run that explains it, which is also what makes the Phase 7
    /// retention rule expressible as "delete the run".
    /// </summary>
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

    /// <summary>
    /// The decision from docs/phase-6/Crawl_Schema_And_Comparison.md, verified rather than assumed:
    /// the broken-link filter is served by <c>ix_crawl_link_result_run_classification</c> on the
    /// result row, with no join back to <c>crawl_run</c>. Captured before the 6.8 views are written,
    /// which is the whole point — Phase 5 lost time to finding this out afterwards.
    /// </summary>
    private static async Task VerifyReportingIndexServesTheFilterAsync(
        string connectionString,
        Guid endpointId)
    {
        // The run the plan is captured against is the one seeding just created. Re-deriving it from
        // a stored seed string would make the assertion depend on how a fixture serialises itself,
        // which is how it silently found no row at all.
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

    /// <summary>Seeds the plan fixture and returns the id of the last run it created.</summary>
    private static async Task<Guid> SeedPlanEvidenceAsync(string connectionString, Guid endpointId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var runId = Guid.Empty;
        for (var run = 0; run < PlanEvidenceRuns; run++)
        {
            runId = await InsertRunAsync(connectionString, endpointId, seedPrefix: "https://plan.test");

            // One statement per run rather than per row: this is fixture volume, and a round trip
            // per row would add minutes to a gate that has to stay fast enough to be run.
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
                    @granted, @refused, 'Canonicalize', 1000, 5, false, now(), @finished);
            """, connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("endpoint", endpointId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("stop_reason", stopReason);
        command.Parameters.AddWithValue("granted", overrideGranted);
        command.Parameters.AddWithValue("refused", (object?)refusedBecause ?? DBNull.Value);
        command.Parameters.AddWithValue("finished", finished ? DateTimeOffset.UtcNow : DBNull.Value);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.Should().Be(expectedConstraint);
    }

    /// <summary>
    /// The sink and the reader against the real schema: results written per link survive the run
    /// that wrote them (BR-L10), and two runs bucket into new, continuing and resolved.
    /// </summary>
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

        var runs = await reader.ListRunsAsync(endpointId, 2, access);
        runs.Should().HaveCount(2);
        runs[0].RunId.Should().Be(currentRun);
        runs[0].BrokenLinkCount.Should().Be(2);
        runs[0].CoveredWholeScope.Should().BeTrue();

        await VerifyRunsAreInvisibleWithoutAccessAsync(services, endpointId, currentRun);
        await VerifyComparisonIsBoundedAsync(services, endpointId);
        await VerifyPartialRunIsNeverABaselineAsync(services, endpointId, currentRun);
        await VerifyUncheckedLinkIsNotReportedResolvedAsync(services, endpointId);
        await VerifyRunStartIsReplayableAsync(services, endpointId);
    }

    /// <summary>
    /// The comparison counts every link but renders a bounded sample. Loading both runs in full to
    /// subtract them in memory would make one page request an unbounded read; truncating the *set*
    /// instead of the display would be worse, because a previous run cut short reports its missing
    /// links as resolved.
    /// </summary>
    private static async Task VerifyComparisonIsBoundedAsync(IServiceProvider services, Guid endpointId)
    {
        var oversize = CrawlReportReader.ComparisonSampleSize + 12;
        var previousRun = Guid.CreateVersion7();
        var currentRun = Guid.CreateVersion7();

        // Every link is broken in the current run and healthy in the previous one, so they all land
        // in a single bucket and the bound is what limits the response rather than the data.
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

    /// <summary>
    /// The reader scopes by visibility, not by the id it was handed. A viewer with no grants must
    /// read another client's runs as absent — an endpoint id in a URL is a parameter, not a
    /// permission, and the shell tests can only prove the route is reachable, not that the query
    /// is scoped.
    /// </summary>
    private static async Task VerifyRunsAreInvisibleWithoutAccessAsync(
        IServiceProvider services,
        Guid endpointId,
        Guid knownRunId)
    {
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICrawlReportReader>();

        // A viewer with no access grants at all: authenticated, entitled to nothing.
        var strangerAccess = new RegistryAccessContext(Guid.CreateVersion7(), [ApplicationRoles.Viewer]);

        (await reader.ListRunsAsync(endpointId, 10, strangerAccess)).Should().BeEmpty(
            "an endpoint id is a parameter, not a permission");
        (await reader.FindRunAsync(knownRunId, strangerAccess)).Should().BeNull();
        (await reader.ListBrokenLinksAsync(knownRunId, 100, strangerAccess)).Should().BeEmpty();
        (await reader.CompareLatestAsync(endpointId, strangerAccess)).CurrentRunId.Should().BeNull();

        // The same reader with a real administrator still sees them, so the assertion above is
        // about the scope rather than about an empty database.
        var administrator = await AdministratorAccessAsync(scope);
        (await reader.ListRunsAsync(endpointId, 10, administrator)).Should().NotBeEmpty();
        (await reader.FindRunAsync(knownRunId, administrator)).Should().NotBeNull();
    }

    /// <summary>
    /// A run that stopped on a budget covered part of the site, so every link it never reached
    /// would look resolved. It must not become the current side of a comparison — the reason a
    /// cancelled run is excluded applies just as much to a page limit.
    /// </summary>
    private static async Task VerifyPartialRunIsNeverABaselineAsync(
        IServiceProvider services,
        Guid endpointId,
        Guid expectedCurrentRun)
    {
        var partialRun = Guid.CreateVersion7();
        await WriteRunAsync(services, endpointId, partialRun, CrawlStopReasons.PageLimit);

        await using var scope = services.CreateAsyncScope();
        var comparison = await scope.ServiceProvider.GetRequiredService<ICrawlReportReader>()
            .CompareLatestAsync(endpointId, await AdministratorAccessAsync(scope));

        comparison.CurrentRunId.Should().Be(expectedCurrentRun,
            "a page-limited run must not displace the last full-scope run as the current side");
    }

    /// <summary>
    /// A previously broken link that timed out this time has not been shown to work. Reporting it
    /// as resolved would close a finding on evidence the crawl never gathered.
    /// </summary>
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

    /// <summary>Replaying a run start is a no-op, matching how link writes tolerate replay.</summary>
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

    /// <summary>
    /// The reader scopes every read to what the requester may see, so these assertions need a real
    /// access context rather than an id alone. The bootstrap administrator has global visibility,
    /// which keeps this stage about the schema; the per-role checks live in the shell tests.
    /// </summary>
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

        await sink.RecordRunOutcomeAsync(new(
            runId, CrawlRunStatuses.Completed, stopReason,
            links.Length, links.Length, false, CrawlOverrideRefusals.NotRequested, []));
    }
}
