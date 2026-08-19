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
    public async Task BeginRunAsync(CrawlRunStart start, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(start);

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
            RobotsOverrideGranted = false,
            RobotsOverrideRefusedBecause = CrawlOverrideRefusals.NotRequested,
            StartedAt = start.StartedAt
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordLinkAsync(
        CrawlLinkRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        dbContext.CrawlLinkResults.Add(new CrawlLinkResult
        {
            Id = Guid.CreateVersion7(),
            RunId = record.RunId,
            SourceUrl = Bounded(record.SourceUrl, CrawlUrlOptions.MaxUrlLength),
            SourceUrlHash = record.SourceUrl is null ? null : Hash(record.SourceUrl),
            TargetUrl = Bounded(record.TargetUrl, CrawlUrlOptions.MaxUrlLength)!,
            TargetUrlHash = Hash(record.TargetUrl),
            Classification = record.Classification,
            SkipReason = record.SkipReason,
            StatusCode = record.StatusCode,
            RedirectCount = record.RedirectCount,
            FinalUrl = Bounded(record.FinalUrl, CrawlUrlOptions.MaxUrlLength),
            IsInternal = record.IsInternal,
            Depth = record.Depth,
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
