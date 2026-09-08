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
    IEndpointTestGate testGate,
    ILogicalCheckQueue logicalCheckQueue,
    MonitoringSchedulingOptions schedulingOptions,
    TimeProvider timeProvider,
    ILogger<ManualCheckService> logger) : IManualCheckService
{
    public async Task<ManualCheckResult> RunNowAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await RunMonitorAsync(
            endpointId, RegistryDefaults.HttpAvailabilityMonitorType, access, cancellationToken);

    public async Task<ManualCheckResult> RunCertificateNowAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await RunMonitorAsync(
            endpointId, RegistryDefaults.SslCertificateMonitorType, access, cancellationToken);

    private async Task<ManualCheckResult> RunMonitorAsync(
        Guid endpointId,
        string monitorType,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        if (!schedulingOptions.Enabled)
        {
            return ManualCheckResult.SchedulingUnavailable();
        }

        if (!await testGate.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            return ManualCheckResult.Forbidden(
                await testGate.DescribeTestBlockAsync(endpointId, access, cancellationToken));
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var monitor = await dbContext.EndpointMonitors
            .Include(candidate => candidate.Endpoint).ThenInclude(endpoint => endpoint.Environment)
            .Where(candidate => candidate.EndpointId == endpointId
                && candidate.MonitorType == monitorType
                && candidate.DeletedAt == null)
            .SingleOrDefaultAsync(cancellationToken);
        if (monitor is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ManualCheckResult.MonitorNotAvailable();
        }

        await CheckConfigurationSnapshotFactory.LockAndRefreshAsync(dbContext, monitor, cancellationToken);
        if (!await testGate.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ManualCheckResult.Forbidden(
                await testGate.DescribeTestBlockAsync(endpointId, access, cancellationToken));
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
            WorkKind = MonitorWorkKinds.For(monitor.MonitorType),
            DedupeKey = MonitorWorkKinds.CreateDedupeKey(logicalCheckId, monitor.MonitorType),
            QueueName = MonitoringQueueNames.ShortChecks,
            State = DurableWorkStates.Dispatching,
            AvailableAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await DurableWorkEnqueueAcknowledgement.TryEnqueueAsync(
            dbContext, logicalCheckQueue, timeProvider, logger, logicalCheckId, durableWorkId);

        return ManualCheckResult.Queued(logicalCheckId);
    }
}
