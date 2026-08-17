using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class LogicalCheckExecutionService(
    ApplicationDbContext dbContext,
    IMonitoringEligibilityService eligibilityService,
    IExecutionLeaseService leaseService,
    ISafeHttpTransport transport,
    ILogicalCheckFinalizationService finalizationService,
    TimeProvider timeProvider,
    ILogger<LogicalCheckExecutionService> logger) : ILogicalCheckExecutionService
{
    private static readonly TimeSpan LeaseBuffer = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumLeaseDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CancellationCleanupTimeout = TimeSpan.FromSeconds(5);

    public async Task<LogicalCheckExecutionStatus> ExecuteAsync(
        ExecuteLogicalCheck command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var check = await LoadCheckAsync(command.LogicalCheckId, cancellationToken)
            ?? throw new InvalidOperationException("The logical check does not exist.");
        using var scope = BeginLogScope(check, command);
        if (check.State == LogicalCheckStates.Completed)
        {
            return LogicalCheckExecutionStatus.AlreadyCompleted;
        }

        EnsureExecutable(check, command.DurableWorkId);
        var request = CreateRequest(check);
        var isEligible = await eligibilityService.IsEndpointEligibleAsync(
            check.EndpointMonitor.EndpointId, cancellationToken);
        var claim = await AcquireLeaseAsync(check, cancellationToken);
        if (claim is null)
        {
            return command.IsFinalAttempt
                ? LogicalCheckExecutionStatus.ReconciliationRequired
                : LogicalCheckExecutionStatus.RetryRequired;
        }

        ExecutionAttempt? attempt;
        try
        {
            attempt = await StartAttemptAsync(check, command, cancellationToken);
        }
        catch
        {
            await leaseService.ReleaseAsync(claim, CancellationToken.None);
            throw;
        }

        if (attempt is null)
        {
            await leaseService.ReleaseAsync(claim, CancellationToken.None);
            return LogicalCheckExecutionStatus.AlreadyCompleted;
        }

        if (!isEligible)
        {
            return await FinalizeAsync(
                claim, attempt.Id, command.DurableWorkId,
                new ExecutionTerminalEvidence(ExecutionTerminalReason.TargetIneligible),
                command.IsFinalAttempt, cancellationToken);
        }

        var result = await transport.SendAsync(request, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            await PrepareCancelledRetryAsync(claim, attempt.Id, command.DurableWorkId);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await FinalizeAsync(
            claim, attempt.Id, command.DurableWorkId,
            new HttpTransportEvidence(request, result),
            command.IsFinalAttempt, cancellationToken);
    }

    private Task<LogicalCheck?> LoadCheckAsync(Guid checkId, CancellationToken token) =>
        dbContext.LogicalChecks
            .Include(check => check.ConfigurationSnapshot)
            .Include(check => check.EndpointMonitor)
                .ThenInclude(monitor => monitor.Endpoint)
                    .ThenInclude(endpoint => endpoint.Environment)
            .Include(check => check.DurableWork)
            .SingleOrDefaultAsync(check => check.Id == checkId, token);

    private Task<ExecutionLeaseClaim?> AcquireLeaseAsync(
        LogicalCheck check,
        CancellationToken token) =>
        leaseService.TryAcquireAsync(new(
            check.EndpointMonitorId, check.Id, Guid.NewGuid(),
            LeaseDuration(check.ConfigurationSnapshot.TimeoutSeconds)), token);

    private async Task<ExecutionAttempt?> StartAttemptAsync(
        LogicalCheck check,
        ExecuteLogicalCheck command,
        CancellationToken token)
    {
        await dbContext.Entry(check).ReloadAsync(token);
        if (check.State == LogicalCheckStates.Completed)
        {
            return null;
        }

        EnsureExecutable(check, command.DurableWorkId);
        var now = timeProvider.GetUtcNow();
        if (check.State == LogicalCheckStates.Queued)
        {
            check.State = LogicalCheckStates.Running;
            check.StartedAt = now;
        }

        await SupersedeAbandonedAttemptsAsync(check.Id, now, token);
        var attempt = new ExecutionAttempt
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = check.Id,
            AttemptNumber = await NextAttemptNumberAsync(check.Id, token),
            JobId = command.JobId,
            WorkerId = command.WorkerId,
            StartedAt = now,
            InfrastructureOutcome = ExecutionAttemptOutcomes.Running
        };
        dbContext.ExecutionAttempts.Add(attempt);
        MarkWorkProcessing(check, command.DurableWorkId, now);
        await dbContext.SaveChangesAsync(token);
        return attempt;
    }

    private async Task SupersedeAbandonedAttemptsAsync(
        Guid checkId,
        DateTimeOffset now,
        CancellationToken token)
    {
        var abandoned = await dbContext.ExecutionAttempts
            .Where(attempt => attempt.LogicalCheckId == checkId
                && attempt.InfrastructureOutcome == ExecutionAttemptOutcomes.Running)
            .ToArrayAsync(token);
        foreach (var attempt in abandoned)
        {
            attempt.InfrastructureOutcome = ExecutionAttemptOutcomes.Superseded;
            attempt.FailureCategory = "LeaseSuperseded";
            attempt.FinishedAt = now;
        }
    }

    private async Task<LogicalCheckExecutionStatus> FinalizeAsync(
        ExecutionLeaseClaim claim,
        Guid attemptId,
        Guid workId,
        LogicalCheckTerminalEvidence evidence,
        bool isFinalAttempt,
        CancellationToken token)
    {
        var status = await finalizationService.FinalizeAsync(new(
            claim, attemptId, workId, evidence), token);
        return status switch
        {
            LogicalCheckFinalizationStatus.Finalized => LogicalCheckExecutionStatus.Completed,
            LogicalCheckFinalizationStatus.AlreadyFinalized => LogicalCheckExecutionStatus.AlreadyCompleted,
            LogicalCheckFinalizationStatus.LeaseLost when isFinalAttempt =>
                LogicalCheckExecutionStatus.ReconciliationRequired,
            LogicalCheckFinalizationStatus.LeaseLost => LogicalCheckExecutionStatus.RetryRequired,
            _ => throw new InvalidOperationException($"Logical check finalization failed with {status}.")
        };
    }

    private async Task PrepareCancelledRetryAsync(
        ExecutionLeaseClaim claim,
        Guid attemptId,
        Guid workId)
    {
        using var cleanup = new CancellationTokenSource(CancellationCleanupTimeout);
        var status = await finalizationService.PrepareRetryAsync(new(
            claim, attemptId, workId, "WorkerCancellation"), cleanup.Token);
        logger.LogInformation("Worker cancellation cleanup completed with {RetryStatus}.", status);
    }

    private async Task<int> NextAttemptNumberAsync(Guid checkId, CancellationToken token)
    {
        var current = await dbContext.ExecutionAttempts
            .Where(attempt => attempt.LogicalCheckId == checkId)
            .Select(attempt => attempt.AttemptNumber)
            .DefaultIfEmpty()
            .MaxAsync(token);
        return current + 1;
    }

    private static SafeHttpTransportRequest CreateRequest(LogicalCheck check)
    {
        var snapshot = check.ConfigurationSnapshot;
        var endpoint = check.EndpointMonitor.Endpoint;
        return new(
            endpoint.Id, endpoint.NormalizedUrl, endpoint.Environment.IsProduction,
            snapshot.MaxRedirects, snapshot.MaxResponseBodyBytes, snapshot.TimeoutSeconds);
    }

    private static TimeSpan LeaseDuration(int timeoutSeconds) =>
        TimeSpan.FromTicks(Math.Min(
            TimeSpan.FromSeconds(timeoutSeconds).Add(LeaseBuffer).Ticks,
            MaximumLeaseDuration.Ticks));

    private static void MarkWorkProcessing(LogicalCheck check, Guid workId, DateTimeOffset now)
    {
        var work = check.DurableWork.Single(candidate =>
            candidate.Id == workId && candidate.WorkKind == DurableWorkKinds.HttpCheck);
        work.State = DurableWorkStates.Processing;
        work.AttemptCount++;
        work.LastFailureCategory = null;
        work.LastFailureAt = null;
        work.UpdatedAt = now;
    }

    private IDisposable? BeginLogScope(LogicalCheck check, ExecuteLogicalCheck command) =>
        logger.BeginScope(new Dictionary<string, object>
        {
            ["LogicalCheckId"] = check.Id,
            ["DurableWorkId"] = command.DurableWorkId,
            ["EndpointId"] = check.EndpointMonitor.EndpointId,
            ["JobId"] = command.JobId
        });

    private static void EnsureExecutable(LogicalCheck check, Guid workId)
    {
        if (check.ConfigurationSnapshot is null
            || check.State is not (LogicalCheckStates.Queued or LogicalCheckStates.Running)
            || !check.DurableWork.Any(work =>
                work.Id == workId && work.WorkKind == DurableWorkKinds.HttpCheck))
        {
            throw new InvalidOperationException("The logical check is not ready for HTTP execution.");
        }
    }

    private static void Validate(ExecuteLogicalCheck command)
    {
        if (command.LogicalCheckId == Guid.Empty
            || command.DurableWorkId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.JobId)
            || command.JobId.Length > 100
            || string.IsNullOrWhiteSpace(command.WorkerId)
            || command.WorkerId.Length > 100)
        {
            throw new ArgumentException("The logical check execution command is invalid.", nameof(command));
        }
    }
}
