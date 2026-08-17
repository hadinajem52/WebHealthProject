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
    MonitoringSchedulingOptions schedulingOptions,
    TimeProvider timeProvider,
    ILogger<ManualCheckService> logger) : IManualCheckService
{
    public async Task<ManualCheckResult> RunNowAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!schedulingOptions.Enabled)
        {
            return ManualCheckResult.SchedulingUnavailable();
        }

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

        // Re-verify authorization immediately before persisting the request, inside the same
        // transaction as the monitor read. This narrows (does not eliminate) the window between
        // the initial check above and commit during which an assignment, evidence grant, or the
        // endpoint itself could be revoked/disabled.
        if (!await targetAuthorization.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ManualCheckResult.Forbidden();
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
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // The transaction commit above is this operation's success boundary: the check is durably
        // queued from here on, regardless of what happens next (including caller cancellation).
        await DurableWorkEnqueueAcknowledgement.TryEnqueueAsync(
            dbContext, logicalCheckQueue, timeProvider, logger, logicalCheckId, durableWorkId);

        return ManualCheckResult.Queued(logicalCheckId);
    }
}
