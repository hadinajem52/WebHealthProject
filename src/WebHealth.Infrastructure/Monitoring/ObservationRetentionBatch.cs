using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class ObservationRetentionBatch(ApplicationDbContext database, MonitoringRetentionOptions options,
    TimeProvider timeProvider, ILogger<ObservationRetentionBatch> logger)
{
    public Task<RetentionBatchResult> ExecuteSeoAsync(CancellationToken cancellationToken = default) => ExecuteAsync(false, cancellationToken);

    public Task<RetentionBatchResult> ExecuteCertificateAsync(CancellationToken cancellationToken = default) => ExecuteAsync(true, cancellationToken);

    private async Task<RetentionBatchResult> ExecuteAsync(bool certificate, CancellationToken cancellationToken)
    {
        options.Validate();
        if (!options.Enabled) return new(0, 0);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.MaximumRunDuration);
        var token = deadline.Token;
        var stopwatch = Stopwatch.StartNew();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await RetentionTransactionLock.AcquireAsync(database, token);
        var now = timeProvider.GetUtcNow();
        var cutoff = certificate ? now.AddMonths(-24) : now.AddDays(-90);
        var checks = new RetentionHistoryQueries(database, now).EligibleCompletedCheckIds(cutoff);
        var seo = database.SeoObservations.Where(item => item.ObservedAt < cutoff && checks.Contains(item.LogicalCheckId)
            && database.SeoObservations.Any(newer => newer.EndpointMonitorId == item.EndpointMonitorId && newer.ObservedAt > item.ObservedAt)
            && (item.LogicalCheck.Result == null || item.LogicalCheck.Result.CurrentStateDisposition != "Current"
                || database.SeoObservations.Any(newer => newer.EndpointMonitorId == item.EndpointMonitorId
                    && newer.ObservedAt > item.ObservedAt && newer.LogicalCheck.Result != null
                    && newer.LogicalCheck.Result.CurrentStateDisposition == "Current")));
        var certificates = database.CertificateObservations.Where(item => item.ObservedAt < cutoff && checks.Contains(item.LogicalCheckId)
            && database.CertificateObservations.Any(newer => newer.EndpointMonitorId == item.EndpointMonitorId && newer.ObservedAt > item.ObservedAt)
            && (item.LogicalCheck.Result == null || item.LogicalCheck.Result.CurrentStateDisposition != "Current"
                || database.CertificateObservations.Any(newer => newer.EndpointMonitorId == item.EndpointMonitorId
                    && newer.ObservedAt > item.ObservedAt && newer.LogicalCheck.Result != null
                    && newer.LogicalCheck.Result.CurrentStateDisposition == "Current")));
        var candidates = certificate
            ? certificates.OrderBy(item => item.ObservedAt).ThenBy(item => item.LogicalCheckId).Select(item => item.LogicalCheckId)
            : seo.OrderBy(item => item.ObservedAt).ThenBy(item => item.LogicalCheckId).Select(item => item.LogicalCheckId);
        var ids = await candidates.Take(options.BatchSize).ToArrayAsync(token);
        var deleted = 0;
        if (!options.DryRun && ids.Length > 0)
        {
            await database.Database.ExecuteSqlRawAsync("SET LOCAL web_health.monitoring_retention = 'on'", token);
            deleted = certificate
                ? await certificates.Where(item => ids.Contains(item.LogicalCheckId)).ExecuteDeleteAsync(token)
                : await seo.Where(item => ids.Contains(item.LogicalCheckId)).ExecuteDeleteAsync(token);
        }
        await transaction.CommitAsync(token);
        logger.LogInformation("Retention {Operation} selected {SelectedCount} and deleted {DeletedCount} rows in {DurationMs} ms; dry run {DryRun}",
            certificate ? "certificate-observation" : "seo-observation", ids.Length, deleted, stopwatch.ElapsedMilliseconds, options.DryRun);
        return new(ids.Length, deleted);
    }
}
