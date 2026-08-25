using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Crawling;

internal sealed class CrawlResultSink(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    TimeProvider timeProvider) : ICrawlResultSink
{
    private const int LinkBatchSize = 250;

    /// <summary>
    /// Opens the run. Replaying the same run id is a controlled no-op rather than a primary-key
    /// failure: link writes already tolerate duplicate delivery, and a start that threw on replay
    /// would make the one operation that cannot be retried the first one in the sequence.
    /// <para>
    /// A replay carrying a different endpoint is refused. That is not a retry of this run, it is a
    /// different crawl reusing an id, and accepting it would attach one crawl's results to another
    /// crawl's target.
    /// </para>
    /// </summary>
    public async Task BeginRunAsync(CrawlRunStart start, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.CrawlRuns.AsNoTracking()
            .SingleOrDefaultAsync(run => run.Id == start.RunId, cancellationToken);
        if (existing is not null)
        {
            if (existing.EndpointId != start.EndpointId)
            {
                throw new InvalidOperationException(
                    $"Crawl run {start.RunId} already exists for a different endpoint.");
            }

            return;
        }

        // The run opens as Running with no finish time, so a process that dies mid-crawl leaves a
        // visibly unfinished run rather than one that reads as complete. `stop_reason` has no null
        // state, so an in-flight run carries the reason it would stop with if the frontier drained.
        dbContext.CrawlRuns.Add(new CrawlRun
        {
            Id = start.RunId,
            EndpointId = start.EndpointId,
            Status = CrawlRunStatuses.Running,
            StopReason = CrawlStopReasons.FrontierExhausted,
            SeedUrls = Bounded(string.Join('\n', start.SeedUrls.Select(seed =>
                CrawlUrlRedactor.Redact(seed, new CrawlUrlOptions
                {
                    SensitiveQueryParameters = start.Settings.SensitiveQueryParameters
                }))), CrawlRunConfiguration.MaxSeedUrlsLength)!,
            AllowedHosts = Scope(start.Settings.AllowedHosts),
            AllowedPathPrefixes = Scope(start.Settings.AllowedPathPrefixes),
            QueryPolicy = start.Settings.QueryPolicy,
            MaxPages = start.Settings.MaxPages,
            MaxDepth = start.Settings.MaxDepth,
            CheckExternalLinks = start.Settings.CheckExternalLinks,
            RobotsOverrideGranted = false,
            RobotsOverrideRefusedBecause = CrawlOverrideRefusals.NotRequested,
            StartedAt = start.StartedAt
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsViolationOf(exception, "pk_crawl_run"))
        {
            // Two callers opening the same run id at once: the row that won is the same row.
            //
            // Matched by constraint name rather than by "any unique violation". crawl_run also
            // carries ux_crawl_run_active, which says this endpoint already has a crawl in
            // flight -- a different fact entirely, and one the caller must see. Swallowing it
            // here would report a run as opened that was never inserted, and the caller would
            // enqueue a job against a row that does not exist.
        }
    }

    /// <summary>
    /// One UPDATE, so two deliveries of the same job cannot both read "unclaimed" and both crawl.
    /// The claim is never taken from a run that already carries one: this is a claim rather than a
    /// lease because a crawl has no legitimate second attempt -- re-running it would repeat every
    /// request against a site we do not own, which is exactly what this phase's limits prevent.
    /// The reconciliation sweep is what releases the endpoint, by closing the row.
    /// </summary>
    public async Task<bool> TryClaimRunAsync(
        Guid runId,
        Guid executionClaimId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var claimed = await dbContext.CrawlRuns
            .Where(run => run.Id == runId
                && run.Status == CrawlRunStatuses.Running
                && run.ExecutionClaimId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(run => run.ExecutionClaimId, executionClaimId),
                cancellationToken);
        return claimed == 1;
    }

    /// <summary>An empty scope list means "derived from the seeds", which is stored as null.</summary>
    private static string? Scope(IReadOnlyList<string> values) =>
        values.Count == 0
            ? null
            : Bounded(string.Join('\n', values), CrawlRunConfiguration.MaxScopeLength);

    public async Task RecordLinkAsync(
        CrawlLinkRecord record,
        CancellationToken cancellationToken = default) =>
        await RecordLinksAsync([record], cancellationToken);

    public async Task<int> RecordLinksAsync(
        IReadOnlyList<CrawlLinkRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        for (var offset = 0; offset < records.Count; offset += LinkBatchSize)
        {
            await WriteBatchAsync(
                records.Skip(offset).Take(LinkBatchSize).ToArray(), cancellationToken);
        }

        if (records.Count == 0)
        {
            return 0;
        }

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var runId = records[0].RunId;
        return await dbContext.CrawlLinkResults.CountAsync(
            result => result.RunId == runId,
            cancellationToken);
    }

    private async Task<int> WriteBatchAsync(
        IReadOnlyList<CrawlLinkRecord> records,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var batch = new NpgsqlBatch(
            (NpgsqlConnection)dbContext.Database.GetDbConnection());
        foreach (var record in records)
        {
            var sourceUrl = Bounded(
                CrawlUrlRedactor.Redact(record.SourceUrl, CrawlUrlOptions.Default),
                CrawlUrlOptions.MaxUrlLength);
            var targetUrl = Bounded(
                CrawlUrlRedactor.Redact(record.TargetUrl, CrawlUrlOptions.Default),
                CrawlUrlOptions.MaxUrlLength)!;
            var sourceIdentity = Bounded(
                record.SourceUrlIdentity ?? record.SourceUrl, CrawlUrlOptions.MaxUrlLength);
            var targetIdentity = Bounded(
                record.TargetUrlIdentity ?? record.TargetUrl, CrawlUrlOptions.MaxUrlLength)!;
            var command = new NpgsqlBatchCommand("""
                INSERT INTO web_health.crawl_link_result
                    (id, run_id, source_url, source_url_hash, target_url, target_url_hash,
                     classification, skip_reason, status_code, redirect_count, final_url,
                     is_internal, depth, duration_ms, recorded_at)
                VALUES
                    (@id, @run_id, @source_url, @source_url_hash, @target_url, @target_url_hash,
                     @classification, @skip_reason, @status_code, @redirect_count, @final_url,
                     @is_internal, @depth, @duration_ms, @recorded_at)
                ON CONFLICT (run_id, source_url_hash, target_url_hash) DO NOTHING
                """);
            command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            command.Parameters.AddWithValue("run_id", NpgsqlDbType.Uuid, record.RunId);
            command.Parameters.AddWithValue("source_url", NpgsqlDbType.Varchar, (object?)sourceUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("source_url_hash", NpgsqlDbType.Bytea,
                sourceIdentity is null ? DBNull.Value : Hash(sourceIdentity));
            command.Parameters.AddWithValue("target_url", NpgsqlDbType.Varchar, targetUrl);
            command.Parameters.AddWithValue("target_url_hash", NpgsqlDbType.Bytea, Hash(targetIdentity));
            command.Parameters.AddWithValue("classification", NpgsqlDbType.Varchar, record.Classification);
            command.Parameters.AddWithValue("skip_reason", NpgsqlDbType.Varchar,
                (object?)record.SkipReason ?? DBNull.Value);
            command.Parameters.AddWithValue("status_code", NpgsqlDbType.Integer,
                (object?)record.StatusCode ?? DBNull.Value);
            command.Parameters.AddWithValue("redirect_count", NpgsqlDbType.Integer, record.RedirectCount);
            command.Parameters.AddWithValue("final_url", NpgsqlDbType.Varchar,
                (object?)Bounded(
                    CrawlUrlRedactor.Redact(record.FinalUrl, CrawlUrlOptions.Default),
                    CrawlUrlOptions.MaxUrlLength) ?? DBNull.Value);
            command.Parameters.AddWithValue("is_internal", NpgsqlDbType.Boolean, record.IsInternal);
            command.Parameters.AddWithValue("depth", NpgsqlDbType.Integer, record.Depth);
            command.Parameters.AddWithValue("duration_ms", NpgsqlDbType.Integer,
                (object?)record.DurationMs ?? DBNull.Value);
            command.Parameters.AddWithValue("recorded_at", NpgsqlDbType.TimestampTz, timeProvider.GetUtcNow());
            batch.BatchCommands.Add(command);
        }

        return await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Written as one conditional UPDATE rather than read-modify-save. The condition is the whole
    /// point: a run this execution no longer owns, or one the reconciliation sweep has already
    /// closed, must not be reopened as Completed by a worker that took longer than the sweep was
    /// willing to wait.
    /// </summary>
    public async Task<bool> RecordRunOutcomeAsync(
        CrawlRunOutcome outcome,
        Guid executionClaimId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var refusedBecause = outcome.RobotsOverrideGranted
            ? null
            : outcome.RobotsOverrideRefusedBecause ?? CrawlOverrideRefusals.NotRequested;
        var failureReason = outcome.Status == CrawlRunStatuses.Failed
            ? Bounded(
                outcome.ValidationErrors.Count > 0
                    ? string.Join(" ", outcome.ValidationErrors)
                    : outcome.FailureCode ?? CrawlFailureCodes.Unexpected,
                CrawlRunConfiguration.MaxFailureReasonLength)
            : null;
        var finishedAt = timeProvider.GetUtcNow();

        var written = await dbContext.CrawlRuns
            .Where(run => run.Id == outcome.RunId
                && run.Status == CrawlRunStatuses.Running
                && run.ExecutionClaimId == executionClaimId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, outcome.Status)
                .SetProperty(run => run.StopReason, outcome.StopReason)
                .SetProperty(run => run.PagesFetched, outcome.PagesFetched)
                .SetProperty(run => run.CoverageLimited, outcome.CoverageLimited)
                .SetProperty(run => run.LinksRecorded, outcome.LinksRecorded)
                .SetProperty(run => run.RobotsOverrideGranted, outcome.RobotsOverrideGranted)
                .SetProperty(run => run.RobotsOverrideRefusedBecause, refusedBecause)
                .SetProperty(run => run.FailureReason, failureReason)
                .SetProperty(run => run.FinishedAt, finishedAt),
                cancellationToken);
        return written == 1;
    }

    /// <summary>SHA-256 of the canonical URL: identity, where the text beside it is evidence.</summary>
    public static byte[] Hash(string url) => SHA256.HashData(Encoding.UTF8.GetBytes(url));

    private static string? Bounded(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>
    /// A unique violation of one named constraint. The name is the whole point: this class writes
    /// to two tables carrying three unique indexes between them, and each says something
    /// different. Matching on the SQL state alone made every one of them look like a harmless
    /// replay.
    /// </summary>
    private static bool IsViolationOf(DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgres
        && string.Equals(postgres.ConstraintName, constraintName, StringComparison.Ordinal);
}
