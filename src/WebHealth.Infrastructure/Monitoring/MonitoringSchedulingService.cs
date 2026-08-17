using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class MonitoringSchedulingService(
    ApplicationDbContext dbContext,
    ILogicalCheckQueue logicalCheckQueue,
    MonitoringSchedulingOptions options,
    TimeProvider timeProvider,
    ILogger<MonitoringSchedulingService> logger) : IMonitoringSchedulingService
{
    public async Task<MonitoringDispatchResult> DispatchDueAsync(
        CancellationToken cancellationToken = default)
    {
        var work = await CreateDueWorkAsync(cancellationToken);
        return await EnqueueAsync(work, cancellationToken);
    }

    public async Task<MonitoringDispatchResult> ReconcileAsync(
        CancellationToken cancellationToken = default)
    {
        var work = await ClaimRecoverableWorkAsync(cancellationToken);
        return await EnqueueAsync(work, cancellationToken);
    }

    private async Task<IReadOnlyList<DispatchWork>> CreateDueWorkAsync(CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(token);
        var monitorIds = await ClaimDueMonitorIdsAsync(now, options.DispatchBatchSize, token);
        if (monitorIds.Count == 0)
        {
            await transaction.CommitAsync(token);
            return [];
        }

        var monitors = await dbContext.EndpointMonitors
            .Include(monitor => monitor.Endpoint)
                .ThenInclude(endpoint => endpoint.Environment)
            .Where(monitor => monitorIds.Contains(monitor.Id))
            .OrderBy(monitor => monitor.NextDueAt)
            .ThenBy(monitor => monitor.Id)
            .ToArrayAsync(token);
        var claimedEndpointIds = monitors.Select(monitor => monitor.EndpointId).Distinct().ToArray();
        var eligibleEndpointIds = (await MonitoringEligibility.Apply(dbContext.Endpoints.AsNoTracking(), now)
            .Where(endpoint => claimedEndpointIds.Contains(endpoint.Id))
            .Select(endpoint => endpoint.Id)
            .ToArrayAsync(token)).ToHashSet();

        var work = new List<DispatchWork>();
        foreach (var monitor in monitors)
        {
            if (eligibleEndpointIds.Contains(monitor.EndpointId))
            {
                work.Add(CreateScheduledCheck(monitor, now));
            }
            else
            {
                monitor.NextDueAt = MonitorCadence.GetFirstSlotAfter(
                    monitor.ScheduleAnchor, monitor.IntervalSeconds, now);
            }
        }

        await dbContext.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return work;
    }

    private DispatchWork CreateScheduledCheck(EndpointMonitor monitor, DateTimeOffset now)
    {
        var logicalCheckId = Guid.NewGuid();
        var durableWorkId = Guid.NewGuid();
        var scheduledFor = monitor.NextDueAt;
        var check = new LogicalCheck
        {
            Id = logicalCheckId,
            EndpointMonitorId = monitor.Id,
            Source = LogicalCheckSources.Scheduled,
            ScheduledFor = scheduledFor,
            State = LogicalCheckStates.Queued,
            CadenceKey = MonitorCadence.CreateCadenceKey(monitor.Id, scheduledFor),
            PolicyFingerprint = monitor.ConfigurationFingerprint,
            CreatedAt = now,
            QueuedAt = now
        };
        dbContext.LogicalChecks.Add(check);
        dbContext.CheckConfigurationSnapshots.Add(
            CheckConfigurationSnapshotFactory.Create(monitor, logicalCheckId, now));
        dbContext.DurableWork.Add(new DurableWork
        {
            Id = durableWorkId,
            LogicalCheckId = logicalCheckId,
            WorkKind = DurableWorkKinds.HttpCheck,
            DedupeKey = $"v1|{logicalCheckId:N}|http-check",
            QueueName = MonitoringQueueNames.ShortChecks,
            State = DurableWorkStates.Dispatching,
            AvailableAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        monitor.NextDueAt = MonitorCadence.GetFirstSlotAfter(
            monitor.ScheduleAnchor, monitor.IntervalSeconds, now);
        return new(logicalCheckId, durableWorkId);
    }

    private async Task<IReadOnlyList<DispatchWork>> ClaimRecoverableWorkAsync(CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        var staleBefore = now - options.RecoveryDelay;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(token);
        var workIds = await ClaimRecoverableWorkIdsAsync(
            now, staleBefore, options.RecoveryBatchSize, token);
        if (workIds.Count == 0)
        {
            await transaction.CommitAsync(token);
            return [];
        }

        var work = await dbContext.DurableWork
            .Where(candidate => workIds.Contains(candidate.Id))
            .ToArrayAsync(token);
        foreach (var candidate in work)
        {
            candidate.State = DurableWorkStates.Dispatching;
            candidate.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return work.Select(candidate => new DispatchWork(candidate.LogicalCheckId, candidate.Id)).ToArray();
    }

    private async Task<MonitoringDispatchResult> EnqueueAsync(
        IReadOnlyList<DispatchWork> work,
        CancellationToken token)
    {
        var enqueued = 0;
        foreach (var candidate in work)
        {
            try
            {
                logicalCheckQueue.Enqueue(candidate.LogicalCheckId, candidate.DurableWorkId);
                await AcknowledgeEnqueueAsync(candidate.DurableWorkId, token);
                enqueued++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Logical check enqueue was interrupted for {LogicalCheckId} and will be reconciled.",
                    candidate.LogicalCheckId);
            }
        }

        return new(work.Count, enqueued);
    }

    private async Task<int> AcknowledgeEnqueueAsync(Guid workId, CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        var updated = await dbContext.DurableWork
            .Where(work => work.Id == workId && work.State == DurableWorkStates.Dispatching)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(work => work.State, DurableWorkStates.Enqueued)
                .SetProperty(work => work.UpdatedAt, now), token);
        var tracked = dbContext.DurableWork.Local.SingleOrDefault(work => work.Id == workId);
        if (updated == 1 && tracked is not null)
        {
            tracked.State = DurableWorkStates.Enqueued;
            tracked.UpdatedAt = now;
            var entry = dbContext.Entry(tracked);
            entry.Property(work => work.State).OriginalValue = DurableWorkStates.Enqueued;
            entry.Property(work => work.State).IsModified = false;
            entry.Property(work => work.UpdatedAt).OriginalValue = now;
            entry.Property(work => work.UpdatedAt).IsModified = false;
        }

        return updated;
    }

    private Task<IReadOnlyList<Guid>> ClaimDueMonitorIdsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken token) => ExecuteIdQueryAsync("""
        SELECT monitor.id
        FROM web_health.endpoint_monitor AS monitor
        JOIN web_health.endpoint AS endpoint ON endpoint.id = monitor.endpoint_id
        JOIN web_health.environment AS environment ON environment.id = endpoint.environment_id
        JOIN web_health.website AS website ON website.id = environment.website_id
        JOIN web_health.client AS client ON client.id = website.client_id
        WHERE monitor.monitor_type = 'HttpAvailability'
          AND monitor.deleted_at IS NULL AND monitor.is_enabled
          AND monitor.next_due_at <= @now
        ORDER BY monitor.next_due_at, monitor.id
        FOR UPDATE OF monitor, endpoint, environment, website, client SKIP LOCKED
        LIMIT @limit
        """, now, limit, token);

    private Task<IReadOnlyList<Guid>> ClaimRecoverableWorkIdsAsync(
        DateTimeOffset now,
        DateTimeOffset staleBefore,
        int limit,
        CancellationToken token) => ExecuteIdQueryAsync("""
        SELECT work.id
        FROM web_health.durable_work AS work
        JOIN web_health.logical_check AS check_record ON check_record.id = work.logical_check_id
        LEFT JOIN web_health.execution_lease AS lease
          ON lease.logical_check_id = work.logical_check_id
        WHERE work.work_kind = 'HttpCheck'
          AND check_record.state <> 'Completed'
          AND work.available_at <= @now
          AND (
              work.state = 'Pending'
              OR (work.state IN ('Dispatching', 'Enqueued') AND work.updated_at <= @stale_before)
              OR (work.state = 'Processing' AND work.updated_at <= @stale_before
                  AND (lease.logical_check_id IS NULL OR lease.expires_at <= @now)))
        ORDER BY work.available_at, work.id
        FOR UPDATE OF work SKIP LOCKED
        LIMIT @limit
        """, now, limit, token, staleBefore);

    private async Task<IReadOnlyList<Guid>> ExecuteIdQueryAsync(
        string sql,
        DateTimeOffset now,
        int limit,
        CancellationToken token,
        DateTimeOffset? staleBefore = null)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(token);
        }

        await using var command = new NpgsqlCommand(
            sql,
            connection,
            (NpgsqlTransaction)dbContext.Database.CurrentTransaction!.GetDbTransaction());
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        if (staleBefore is not null)
        {
            command.Parameters.AddWithValue("stale_before", NpgsqlDbType.TimestampTz, staleBefore.Value);
        }

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private sealed record DispatchWork(Guid LogicalCheckId, Guid DurableWorkId);
}
