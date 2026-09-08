using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class MonitoringRuntimeRecorder(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<MonitoringRuntimeRecorder> logger)
{
    public async Task<MonitoringDispatchResult> RunAsync(
        string operation, Func<Task<MonitoringDispatchResult>> action, CancellationToken cancellationToken)
    {
        var invocationId = Guid.NewGuid();
        var startedAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        await using (var database = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO web_health.monitoring_runtime_state
                    (operation, invocation_id, last_started_at, consecutive_failures)
                VALUES ({operation}, {invocationId}, {startedAt}, 0)
                ON CONFLICT (operation) DO UPDATE SET
                    invocation_id = EXCLUDED.invocation_id, last_started_at = EXCLUDED.last_started_at;
                """, cancellationToken);
        }
        try
        {
            var result = await action();
            await CompleteAsync(operation, invocationId, stopwatch.ElapsedMilliseconds,
                result.EnqueuedCount < result.ClaimedCount ? "QueueEnqueue" : null);
            return result;
        }
        catch (Exception exception)
        {
            await CompleteAsync(operation, invocationId, stopwatch.ElapsedMilliseconds, exception switch
            {
                OperationCanceledException => "Cancellation",
                NpgsqlException => "Database",
                DbUpdateException { InnerException: NpgsqlException } => "Database",
                _ => "Unexpected"
            });
            throw;
        }
    }

    private async Task CompleteAsync(string operation, Guid invocationId, long durationMs, string? failureCategory)
    {
        MonitoringTelemetry.Record("Unknown", "Scheduled", operation, failureCategory, durationMs);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(deadline.Token);
            var completedAt = timeProvider.GetUtcNow();
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE web_health.monitoring_runtime_state SET
                    last_succeeded_at = CASE WHEN {failureCategory}::text IS NULL THEN {completedAt} ELSE last_succeeded_at END,
                    last_failed_at = CASE WHEN {failureCategory}::text IS NOT NULL THEN {completedAt} ELSE last_failed_at END,
                    last_duration_ms = {durationMs}, failure_category = {failureCategory},
                    consecutive_failures = CASE WHEN {failureCategory}::text IS NULL THEN 0
                        ELSE least(consecutive_failures::bigint + 1, 2147483647)::integer END
                WHERE operation = {operation} AND invocation_id = {invocationId};
                """, deadline.Token);
            logger.LogInformation("Monitoring operation {Operation} finished in {DurationMs} ms with category {FailureCategory}",
                operation, durationMs, failureCategory ?? "None");
        }
        catch (Exception)
        {
            logger.LogError("Monitoring runtime record could not be saved for {Operation}", operation);
        }
    }
}
