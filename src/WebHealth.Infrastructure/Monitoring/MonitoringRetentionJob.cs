using Hangfire;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Maintenance;

namespace WebHealth.Infrastructure.Monitoring;

public sealed class MonitoringRetentionJob(MonitoringRetentionCoordinator coordinator,
    MonitoringRetentionOptions options, ILogger<MonitoringRetentionJob> logger)
{
    [Queue(MaintenanceQueueNames.Maintenance)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await coordinator.ExecuteAsync(cancellationToken);
            logger.LogInformation("Retention run completed {BatchCount} batches; duration limit {DurationLimitReached}; dry run {DryRun}",
                result.Batches.Count, result.DurationLimitReached, options.DryRun);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Monitoring retention was cancelled.", cancellationToken);
        }
        catch (Exception exception)
        {
            var category = exception.GetType().Name;
            logger.LogError("Retention run failed with category {FailureCategory}; dry run {DryRun}", category, options.DryRun);
            throw new InvalidOperationException("Monitoring retention failed: " + category);
        }
    }
}
