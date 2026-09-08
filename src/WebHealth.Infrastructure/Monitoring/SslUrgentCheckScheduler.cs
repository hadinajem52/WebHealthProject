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

internal sealed class SslUrgentCheckScheduler(
    ApplicationDbContext dbContext,
    ILogicalCheckQueue logicalCheckQueue,
    MonitoringSchedulingOptions schedulingOptions,
    TimeProvider timeProvider,
    ILogger<SslUrgentCheckScheduler> logger) : ISslUrgentCheckScheduler
{
    public async Task<UrgentCertificateCheck?> PrepareAfterTlsFailureAsync(
        Guid endpointId,
        LogicalCheckTerminalEvidence evidence,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!schedulingOptions.Enabled || !IsTlsFailure(evidence))
        {
            return null;
        }

        var monitorId = await LockCertificateMonitorAsync(endpointId, cancellationToken);
        if (monitorId is null)
        {
            return null;
        }

        var monitor = await dbContext.EndpointMonitors
            .Include(candidate => candidate.Endpoint).ThenInclude(endpoint => endpoint.Environment)
            .SingleAsync(candidate => candidate.Id == monitorId.Value, cancellationToken);
        var cooldownStart = now - schedulingOptions.UrgentSslCooldown;
        var recentlyRequested = await dbContext.LogicalChecks.AnyAsync(candidate =>
            candidate.EndpointMonitorId == monitor.Id
            && candidate.Source == LogicalCheckSources.Urgent
            && candidate.CreatedAt >= cooldownStart,
            cancellationToken);
        if (recentlyRequested)
        {
            logger.LogDebug(
                "Urgent certificate check for endpoint {EndpointId} suppressed by cooldown.",
                endpointId);
            return null;
        }

        await CheckConfigurationSnapshotFactory.LockAndRefreshAsync(dbContext, monitor, cancellationToken);
        var logicalCheckId = Guid.NewGuid();
        var durableWorkId = Guid.NewGuid();
        dbContext.LogicalChecks.Add(new LogicalCheck
        {
            Id = logicalCheckId,
            EndpointMonitorId = monitor.Id,
            Source = LogicalCheckSources.Urgent,
            RequestedAt = now,
            State = LogicalCheckStates.Queued,
            PolicyFingerprint = monitor.ConfigurationFingerprint,
            CreatedAt = now,
            QueuedAt = now
        });
        dbContext.CheckConfigurationSnapshots.Add(
            CheckConfigurationSnapshotFactory.Create(monitor, logicalCheckId, now));
        dbContext.DurableWork.Add(new DurableWork
        {
            Id = durableWorkId,
            LogicalCheckId = logicalCheckId,
            WorkKind = DurableWorkKinds.SslCheck,
            DedupeKey = MonitorWorkKinds.CreateDedupeKey(logicalCheckId, monitor.MonitorType),
            QueueName = MonitoringQueueNames.ShortChecks,
            State = DurableWorkStates.Dispatching,
            AvailableAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        return new(logicalCheckId, durableWorkId);
    }

    public async Task EnqueueAsync(
        UrgentCertificateCheck request,
        CancellationToken cancellationToken = default)
    {
        await DurableWorkEnqueueAcknowledgement.TryEnqueueAsync(
            dbContext, logicalCheckQueue, timeProvider, logger,
            request.LogicalCheckId, request.DurableWorkId);
        logger.LogInformation(
            "Urgent certificate check {LogicalCheckId} queued.",
            request.LogicalCheckId);
    }

    private async Task<Guid?> LockCertificateMonitorAsync(
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id FROM web_health.endpoint_monitor
            WHERE endpoint_id = @endpoint_id
              AND monitor_type = @monitor_type
              AND deleted_at IS NULL
              AND is_enabled
            FOR UPDATE
            """;
        await using var command = new NpgsqlCommand(
            sql,
            (NpgsqlConnection)dbContext.Database.GetDbConnection(),
            dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
        command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Uuid, endpointId);
        command.Parameters.AddWithValue(
            "monitor_type", NpgsqlDbType.Text, RegistryDefaults.SslCertificateMonitorType);
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    private static bool IsTlsFailure(LogicalCheckTerminalEvidence evidence) =>
        evidence is HttpTransportEvidence { Result.Failure: SafeHttpFailureKind.Tls };
}
