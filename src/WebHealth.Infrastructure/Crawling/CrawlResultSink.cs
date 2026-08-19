using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// Writes a run and its results as they resolve. Per result rather than batched at the end, which
/// is the whole of BR-L10's preservation guarantee: a cancelled run needs no special save path
/// because everything it found is already committed.
/// </summary>
internal sealed class CrawlResultSink(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : ICrawlResultSink
{
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
            SeedUrls = Bounded(
                string.Join('\n', start.SeedUrls), CrawlRunConfiguration.MaxSeedUrlsLength)!,
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
        catch (DbUpdateException exception) when (IsDuplicatePair(exception))
        {
            // Two callers opening the same run id at once: the row that won is the same row.
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>An empty scope list means "derived from the seeds", which is stored as null.</summary>
    private static string? Scope(IReadOnlyList<string> values) =>
        values.Count == 0
            ? null
            : Bounded(string.Join('\n', values), CrawlRunConfiguration.MaxScopeLength);

    public async Task RecordLinkAsync(
        CrawlLinkRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Bound first, then hash what was bound. Hashing the original while storing a shortened
        // copy would make the identity describe a value the row does not contain — and that identity
        // is exactly what the source-target uniqueness index is built on.
        var sourceUrl = Bounded(record.SourceUrl, CrawlUrlOptions.MaxUrlLength);
        var targetUrl = Bounded(record.TargetUrl, CrawlUrlOptions.MaxUrlLength)!;

        dbContext.CrawlLinkResults.Add(new CrawlLinkResult
        {
            Id = Guid.CreateVersion7(),
            RunId = record.RunId,
            SourceUrl = sourceUrl,
            SourceUrlHash = sourceUrl is null ? null : Hash(sourceUrl),
            TargetUrl = targetUrl,
            TargetUrlHash = Hash(targetUrl),
            Classification = record.Classification,
            SkipReason = record.SkipReason,
            StatusCode = record.StatusCode,
            RedirectCount = record.RedirectCount,
            FinalUrl = Bounded(record.FinalUrl, CrawlUrlOptions.MaxUrlLength),
            IsInternal = record.IsInternal,
            Depth = record.Depth,
            DurationMs = record.DurationMs,
            RecordedAt = timeProvider.GetUtcNow()
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicatePair(exception))
        {
            // BR-L07 is enforced by the index, and the ledger already deduplicates, so reaching
            // here means a retry re-sent a pair rather than that a pair was counted twice. The row
            // that is already stored is the same row, so the write is dropped rather than failed.
            dbContext.ChangeTracker.Clear();
        }
    }

    public async Task RecordRunOutcomeAsync(
        CrawlRunOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        dbContext.ChangeTracker.Clear();
        var run = await dbContext.CrawlRuns
            .SingleOrDefaultAsync(item => item.Id == outcome.RunId, cancellationToken);
        if (run is null) return;

        run.Status = outcome.Status;
        run.StopReason = outcome.StopReason;
        run.PagesFetched = outcome.PagesFetched;
        run.LinksRecorded = outcome.LinksRecorded;
        run.RobotsOverrideGranted = outcome.RobotsOverrideGranted;
        run.RobotsOverrideRefusedBecause = outcome.RobotsOverrideGranted
            ? null
            : outcome.RobotsOverrideRefusedBecause ?? CrawlOverrideRefusals.NotRequested;
        run.FailureReason = outcome.Status == CrawlRunStatuses.Failed
            ? Bounded(
                outcome.ValidationErrors.Count > 0
                    ? string.Join(" ", outcome.ValidationErrors)
                    : "The crawl stopped on an unexpected error.",
                CrawlRunConfiguration.MaxFailureReasonLength)
            : null;
        run.FinishedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>SHA-256 of the canonical URL: identity, where the text beside it is evidence.</summary>
    public static byte[] Hash(string url) => SHA256.HashData(Encoding.UTF8.GetBytes(url));

    private static string? Bounded(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static bool IsDuplicatePair(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
