using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class MonitoringRetentionBatchRunner(IServiceScopeFactory scopeFactory) : IMonitoringRetentionBatchRunner
{
    public async Task<(int Selected, int Deleted)> ExecuteAsync(RetentionCategory category, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var result = category switch
        {
            RetentionCategory.DailyAggregatePreparation => await services.GetRequiredService<DailyAggregatePreparationBatch>().ExecuteAsync(cancellationToken),
            RetentionCategory.IncidentBundles => await services.GetRequiredService<IncidentRetentionBatch>().ExecuteAsync(cancellationToken),
            RetentionCategory.ExecutionAttempts => await services.GetRequiredService<ExecutionHistoryRetentionBatch>().ExecuteAsync(cancellationToken),
            RetentionCategory.DurableWork => await services.GetRequiredService<ExecutionHistoryRetentionBatch>().ExecuteWorkAsync(cancellationToken),
            RetentionCategory.SeoObservations => await services.GetRequiredService<ObservationRetentionBatch>().ExecuteSeoAsync(cancellationToken),
            RetentionCategory.CertificateObservations => await services.GetRequiredService<ObservationRetentionBatch>().ExecuteCertificateAsync(cancellationToken),
            RetentionCategory.RawResults => await services.GetRequiredService<RawResultRetentionBatch>().ExecuteAsync(cancellationToken),
            RetentionCategory.CrawlRuns => await services.GetRequiredService<CrawlRetentionBatch>().ExecuteAsync(cancellationToken),
            RetentionCategory.PageAuditRuns => await services.GetRequiredService<PageAuditRetentionBatch>().ExecuteAsync(cancellationToken),
            RetentionCategory.RobotsSnapshots => await services.GetRequiredService<RobotsRetentionBatch>().ExecuteAsync(cancellationToken),
            RetentionCategory.LogicalChecks => await services.GetRequiredService<LogicalCheckRetentionBatch>().ExecuteAsync(cancellationToken),
            RetentionCategory.DailyAggregates => await services.GetRequiredService<AggregateRetentionBatch>().ExecuteAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
        return (result.Selected, result.Deleted);
    }
}
