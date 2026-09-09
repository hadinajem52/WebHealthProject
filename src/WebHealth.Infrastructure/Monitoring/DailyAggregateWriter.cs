using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Reporting;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class DailyAggregateWriter(ApplicationDbContext database, TimeProvider timeProvider)
{
    public async Task<bool> RecomputeAsync(Guid monitorId, DateOnly day, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (day >= DateOnly.FromDateTime(now.UtcDateTime)) return false;
        await using var ownedTransaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(cancellationToken) : null;
        await RetentionTransactionLock.AcquireAsync(database, cancellationToken);
        var existing = await database.MonitoringDailyAggregates.SingleOrDefaultAsync(
            item => item.EndpointMonitorId == monitorId && item.UtcDate == day, cancellationToken);
        if (existing is not null)
        {
            await database.Entry(existing).ReloadAsync(cancellationToken);
            if (existing.RawDeletionStartedAt is not null) return false;
        }
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = start.AddDays(1);
        var row = new MonitoringDailyAggregate
        {
            EndpointMonitorId = monitorId,
            UtcDate = day,
            ComputedAt = now,
            ComparabilityIdentity = string.Empty,
            LowestSource = string.Empty,
            HighestSource = string.Empty
        };
        using var identityHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string? previousIdentity = null;
        var identityCount = 0;
        var samples = database.CheckResults.AsNoTracking().Where(result => result.EndpointMonitorId == monitorId
            && result.MeasuredAt >= start && result.MeasuredAt < end)
            .OrderBy(result => result.ConfigurationIdentity)
            .ThenBy(result => result.LogicalCheckId)
            .Select(result => new
            {
                result.Outcome,
                result.FailureCategory,
                result.CountsForUptime,
                result.IsMaintenance,
                result.MonitorSource,
                result.TotalDurationMs,
                result.MeasuredAt,
                result.ConfigurationIdentity
            });
        await foreach (var sample in samples.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            row.TotalCount++;
            if (sample.MonitorSource == "Scheduled") row.ScheduledCount++;
            if (sample.IsMaintenance) row.MaintenanceCount++;
            if (sample.Outcome == "Cancelled") row.CancelledCount++;
            if (sample.CountsForUptime)
            {
                row.EligibleCount++;
                if (sample.Outcome == "Healthy") row.HealthyCount++;
                else if (UptimeParticipation.IsAvailable(sample.FailureCategory)) row.WarningCount++;
                else row.DownCount++;
                if (sample.Outcome is "Healthy" or "Warning")
                {
                    row.DurationCount++;
                    row.DurationSumMs = checked(row.DurationSumMs + sample.TotalDurationMs);
                    row.DurationMinimumMs = Math.Min(row.DurationMinimumMs ?? sample.TotalDurationMs, sample.TotalDurationMs);
                    row.DurationMaximumMs = Math.Max(row.DurationMaximumMs ?? sample.TotalDurationMs, sample.TotalDurationMs);
                    row.DurationHistogram[ResponseTimeHistogram.BucketIndex(sample.TotalDurationMs)]++;
                }
            }
            else row.ExcludedCount++;
            row.FirstMeasuredAt = row.TotalCount == 1 || sample.MeasuredAt < row.FirstMeasuredAt ? sample.MeasuredAt : row.FirstMeasuredAt;
            row.LastMeasuredAt = row.TotalCount == 1 || sample.MeasuredAt > row.LastMeasuredAt ? sample.MeasuredAt : row.LastMeasuredAt;
            if (row.TotalCount == 1 || string.CompareOrdinal(sample.MonitorSource, row.LowestSource) < 0) row.LowestSource = sample.MonitorSource;
            if (row.TotalCount == 1 || string.CompareOrdinal(sample.MonitorSource, row.HighestSource) > 0) row.HighestSource = sample.MonitorSource;
            var identity = sample.ConfigurationIdentity;
            if (identity != previousIdentity)
            {
                identityHash.AppendData(Encoding.UTF8.GetBytes(identity));
                identityCount++;
                previousIdentity = identity;
            }
        }
        if (row.TotalCount == 0) return false;
        row.ComparabilityIdentity = Convert.ToHexStringLower(identityHash.GetHashAndReset());
        row.IsComparable = identityCount == 1;
        if (existing is null) database.MonitoringDailyAggregates.Add(row);
        else database.Entry(existing).CurrentValues.SetValues(row);
        await database.SaveChangesAsync(cancellationToken);
        if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
        return true;
    }
}
