namespace WebHealth.Application.Monitoring;

public enum RetentionCategory
{
    DailyAggregatePreparation,
    IncidentBundles,
    ExecutionAttempts,
    DurableWork,
    SeoObservations,
    CertificateObservations,
    RawResults,
    CrawlRuns,
    PageAuditRuns,
    RobotsSnapshots,
    LogicalChecks,
    DailyAggregates
}

public interface IMonitoringRetentionBatchRunner
{
    Task<(int Selected, int Deleted)> ExecuteAsync(RetentionCategory category, CancellationToken cancellationToken);
}

public sealed record RetentionBatchReport(RetentionCategory Category, int Selected, int Deleted);
public sealed record RetentionRunResult(IReadOnlyList<RetentionBatchReport> Batches, bool DurationLimitReached);

public sealed class MonitoringRetentionCoordinator(IMonitoringRetentionBatchRunner runner,
    MonitoringRetentionOptions options, TimeProvider timeProvider)
{
    public async Task<RetentionRunResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (!options.Enabled) return new([], false);
        using var deadline = new CancellationTokenSource(options.MaximumRunDuration, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var started = timeProvider.GetTimestamp();
        var reports = new List<RetentionBatchReport>();
        var attempted = 0;
        while (attempted < options.MaximumBatchesPerRun)
        {
            var deletedThisPass = 0;
            foreach (var category in Enum.GetValues<RetentionCategory>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (deadline.IsCancellationRequested || timeProvider.GetElapsedTime(started) >= options.MaximumRunDuration)
                    return new(reports, true);
                if (attempted >= options.MaximumBatchesPerRun) break;
                attempted++;
                try
                {
                    var result = await runner.ExecuteAsync(category, linked.Token);
                    reports.Add(new(category, result.Selected, result.Deleted));
                    deletedThisPass += result.Deleted;
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return new(reports, true);
                }
            }
            if (options.DryRun || deletedThisPass == 0) break;
        }
        return new(reports, deadline.IsCancellationRequested || timeProvider.GetElapsedTime(started) >= options.MaximumRunDuration);
    }
}
