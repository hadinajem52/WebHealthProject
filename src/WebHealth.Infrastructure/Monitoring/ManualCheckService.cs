using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class ManualCheckService(
    ApplicationDbContext dbContext,
    ITargetAuthorizationService targetAuthorization,
    ILogicalCheckQueue logicalCheckQueue,
    TimeProvider timeProvider,
    ILogger<ManualCheckService> logger) : IManualCheckService
{
    public async Task<ManualCheckResult> RunNowAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!await targetAuthorization.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            return ManualCheckResult.Forbidden();
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var monitor = await dbContext.EndpointMonitors
            .Where(candidate => candidate.EndpointId == endpointId
                && candidate.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType
                && candidate.DeletedAt == null && candidate.IsEnabled)
            .SingleOrDefaultAsync(cancellationToken);
        if (monitor is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ManualCheckResult.MonitorNotAvailable();
        }

        var logicalCheckId = Guid.NewGuid();
        var durableWorkId = Guid.NewGuid();
        dbContext.LogicalChecks.Add(new LogicalCheck
        {
            Id = logicalCheckId,
            EndpointMonitorId = monitor.Id,
            Source = LogicalCheckSources.Manual,
            RequestedAt = now,
            InitiatedByUserId = access.UserId,
            State = LogicalCheckStates.Queued,
            PolicyFingerprint = monitor.ConfigurationFingerprint,
            CreatedAt = now,
            QueuedAt = now
        });
        dbContext.CheckConfigurationSnapshots.Add(
            CheckConfigurationSnapshotFactory.Create(monitor, logicalCheckId, now));
        var work = new DurableWork
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
        };
        dbContext.DurableWork.Add(work);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        try
        {
            logicalCheckQueue.Enqueue(logicalCheckId, durableWorkId);
            work.State = DurableWorkStates.Enqueued;
            work.UpdatedAt = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Manual check enqueue was interrupted for {LogicalCheckId} and will be reconciled.",
                logicalCheckId);
        }

        return ManualCheckResult.Queued(logicalCheckId);
    }
}
