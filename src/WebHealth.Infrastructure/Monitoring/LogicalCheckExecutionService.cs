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
    ISslCertificateProbe sslCertificateProbe,
    ILogicalCheckFinalizationService finalizationService,
    TimeProvider timeProvider,
    ILogger<LogicalCheckExecutionService> logger) : ILogicalCheckExecutionService
{
    private const int MaximumTotalAttempts = 3;
    private static readonly TimeSpan LeaseBuffer = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumLeaseDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CancellationCleanupTimeout = TimeSpan.FromSeconds(5);

    public async Task<LogicalCheckExecutionStatus> ExecuteAsync(
        ExecuteLogicalCheck command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var check = await LoadCheckAsync(command.LogicalCheckId, cancellationToken);
        if (check is null)
        {
            return LogicalCheckExecutionStatus.AlreadyCompleted;
        }
        using var scope = BeginLogScope(check, command);
        if (check.State == LogicalCheckStates.Completed)
        {
            return LogicalCheckExecutionStatus.AlreadyCompleted;
        }

        EnsureExecutable(check, command.DurableWorkId);
        // A paused monitor stops the cadence, so only scheduled work re-checks it here.
        var isEligible = check.Source == LogicalCheckSources.Scheduled
            ? await eligibilityService.IsEndpointEligibleAsync(
                check.EndpointMonitor.EndpointId, cancellationToken)
            : await eligibilityService.IsEndpointTestableAsync(
                check.EndpointMonitor.EndpointId, cancellationToken);
        var attemptNumber = await CountAttemptsAsync(check.Id, cancellationToken) + 1;
        var isFinalAttempt = attemptNumber >= MaximumTotalAttempts;
        var claim = await AcquireLeaseAsync(check, cancellationToken);
        if (claim is null)
        {
            return isFinalAttempt
                ? LogicalCheckExecutionStatus.ReconciliationRequired
                : LogicalCheckExecutionStatus.RetryRequired;
        }

        ExecutionAttempt? attempt;
        try
        {
            attempt = await StartAttemptAsync(check, command, attemptNumber, cancellationToken);
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
                isFinalAttempt, cancellationToken);
        }

        isEligible = check.Source == LogicalCheckSources.Scheduled
            ? await eligibilityService.IsEndpointEligibleAsync(
                check.EndpointMonitor.EndpointId, cancellationToken)
            : await eligibilityService.IsEndpointTestableAsync(
                check.EndpointMonitor.EndpointId, cancellationToken);
        if (!isEligible)
        {
            return await FinalizeAsync(
                claim, attempt.Id, command.DurableWorkId,
                new ExecutionTerminalEvidence(ExecutionTerminalReason.TargetIneligible),
                isFinalAttempt, cancellationToken);
        }

        LogicalCheckTerminalEvidence evidence;
        try
        {
            evidence = await ObserveAsync(check, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Logical check transport execution faulted.");
            if (isFinalAttempt)
            {
                return await FinalizeAsync(
                    claim, attempt.Id, command.DurableWorkId,
                    new ExecutionTerminalEvidence(ExecutionTerminalReason.RetriesExhausted),
                    isFinalAttempt, cancellationToken);
            }

            await PrepareFaultedRetryAsync(claim, attempt.Id, command.DurableWorkId, cancellationToken);
            return LogicalCheckExecutionStatus.RetryRequired;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await PrepareCancelledRetryAsync(claim, attempt.Id, command.DurableWorkId);
            cancellationToken.ThrowIfCancellationRequested();
        }

        // A TLS-related failure also triggers an urgent certificate check (BR-C07); that is
        // created inside the finalization transaction so it shares this result's fate.
        return await FinalizeAsync(
            claim, attempt.Id, command.DurableWorkId, evidence, isFinalAttempt, cancellationToken);
    }

    /// <summary>
    /// The observation each monitor type makes is the only part of execution that differs.
    /// Leasing, attempt accounting, retries and finalization are shared, so an SSL check gets
    /// the same idempotency and recovery guarantees as an availability check.
    /// </summary>
    private async Task<LogicalCheckTerminalEvidence> ObserveAsync(
        LogicalCheck check,
        CancellationToken cancellationToken)
    {
        if (MonitorWorkKinds.IsSsl(check.ConfigurationSnapshot.MonitorType))
        {
            var probeRequest = CreateProbeRequest(check);
            return new SslCertificateEvidence(
                probeRequest, await sslCertificateProbe.ProbeAsync(probeRequest, cancellationToken));
        }

        var request = CreateRequest(check);
        return new HttpTransportEvidence(request, await transport.SendAsync(request, cancellationToken));
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
        int attemptNumber,
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
            AttemptNumber = attemptNumber,
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

    private async Task PrepareFaultedRetryAsync(
        ExecutionLeaseClaim claim,
        Guid attemptId,
        Guid workId,
        CancellationToken token)
    {
        var status = await finalizationService.PrepareRetryAsync(new(
            claim, attemptId, workId, "InfrastructureFault"), token);
        logger.LogInformation("Infrastructure fault retry preparation completed with {RetryStatus}.", status);
    }

    private Task<int> CountAttemptsAsync(Guid checkId, CancellationToken token) =>
        dbContext.ExecutionAttempts.CountAsync(attempt => attempt.LogicalCheckId == checkId, token);

    private static SslCertificateProbeRequest CreateProbeRequest(LogicalCheck check) => new(
        check.EndpointMonitor.Endpoint.Id,
        check.EndpointMonitor.Endpoint.NormalizedUrl,
        check.ConfigurationSnapshot.TimeoutSeconds);

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
            candidate.Id == workId && candidate.WorkKind == ExpectedWorkKind(check));
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

    private static string ExpectedWorkKind(LogicalCheck check) =>
        MonitorWorkKinds.For(check.ConfigurationSnapshot.MonitorType);

    private static void EnsureExecutable(LogicalCheck check, Guid workId)
    {
        if (check.ConfigurationSnapshot is null
            || check.State is not (LogicalCheckStates.Queued or LogicalCheckStates.Running)
            || !check.DurableWork.Any(work =>
                work.Id == workId && work.WorkKind == ExpectedWorkKind(check)))
        {
            throw new InvalidOperationException("The logical check is not ready for execution.");
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
