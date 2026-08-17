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
    IHttpCheckHistoryService historyService,
    TimeProvider timeProvider,
    ILogger<LogicalCheckExecutionService> logger) : ILogicalCheckExecutionService
{
    private static readonly TimeSpan LeaseBuffer = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumLeaseDuration = TimeSpan.FromMinutes(15);

    public async Task<LogicalCheckExecutionStatus> ExecuteAsync(
        ExecuteLogicalCheck command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var check = await LoadCheckAsync(command.LogicalCheckId, cancellationToken);
        if (check is null)
        {
            throw new InvalidOperationException("The logical check does not exist.");
        }

        using var scope = BeginLogScope(check, command);
        if (check.State == LogicalCheckStates.Completed)
        {
            return LogicalCheckExecutionStatus.AlreadyCompleted;
        }

        EnsureExecutable(check);
        var request = CreateRequest(check);
        var isEligible = await eligibilityService.IsEndpointEligibleAsync(
            check.EndpointMonitor.EndpointId,
            cancellationToken);
        var claim = await AcquireLeaseAsync(check, cancellationToken);
        if (claim is null)
        {
            return LogicalCheckExecutionStatus.LeaseUnavailable;
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
            await leaseService.ReleaseAsync(claim, cancellationToken);
            return LogicalCheckExecutionStatus.AlreadyCompleted;
        }

        return isEligible
            ? await ExecuteTransportAsync(check, request, claim, attempt, command, cancellationToken)
            : await CompleteIneligibleAsync(request, claim, attempt, cancellationToken);
    }

    private Task<LogicalCheck?> LoadCheckAsync(Guid logicalCheckId, CancellationToken cancellationToken) =>
        dbContext.LogicalChecks
            .Include(check => check.ConfigurationSnapshot)
            .Include(check => check.EndpointMonitor)
                .ThenInclude(monitor => monitor.Endpoint)
                    .ThenInclude(endpoint => endpoint.Environment)
            .Include(check => check.DurableWork)
            .SingleOrDefaultAsync(check => check.Id == logicalCheckId, cancellationToken);

    private async Task<ExecutionLeaseClaim?> AcquireLeaseAsync(
        LogicalCheck check,
        CancellationToken cancellationToken) =>
        await leaseService.TryAcquireAsync(new(
            check.EndpointMonitorId,
            check.Id,
            Guid.NewGuid(),
            LeaseDuration(check.ConfigurationSnapshot.TimeoutSeconds)),
            cancellationToken);

    private async Task<ExecutionAttempt?> StartAttemptAsync(
        LogicalCheck check,
        ExecuteLogicalCheck command,
        CancellationToken cancellationToken)
    {
        await dbContext.Entry(check).ReloadAsync(cancellationToken);
        if (check.State == LogicalCheckStates.Completed)
        {
            return null;
        }

        EnsureExecutable(check);
        var now = timeProvider.GetUtcNow();
        if (check.State == LogicalCheckStates.Queued)
        {
            check.State = LogicalCheckStates.Running;
            check.StartedAt = now;
        }

        var attempt = new ExecutionAttempt
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = check.Id,
            AttemptNumber = await NextAttemptNumberAsync(check.Id, cancellationToken),
            JobId = command.JobId,
            WorkerId = command.WorkerId,
            StartedAt = now,
            InfrastructureOutcome = ExecutionAttemptOutcomes.Running
        };
        dbContext.ExecutionAttempts.Add(attempt);
        MarkWorkProcessing(check, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return attempt;
    }

    private async Task<LogicalCheckExecutionStatus> ExecuteTransportAsync(
        LogicalCheck check,
        SafeHttpTransportRequest request,
        ExecutionLeaseClaim claim,
        ExecutionAttempt attempt,
        ExecuteLogicalCheck command,
        CancellationToken cancellationToken)
    {
        SafeHttpTransportResult result;
        try
        {
            result = await transport.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Logical check transport attempt failed with {FailureType}.",
                exception.GetType().Name);
            return command.IsFinalAttempt
                ? await CompleteExhaustedAsync(request, claim, attempt, cancellationToken)
                : await PrepareRetryAsync(check, claim, attempt, cancellationToken);
        }

        var outcome = result.Failure == SafeHttpFailureKind.Cancelled
            ? ExecutionAttemptOutcomes.Cancelled
            : ExecutionAttemptOutcomes.Succeeded;
        return await PersistResultAsync(
            request,
            result,
            claim,
            attempt,
            outcome,
            outcome == ExecutionAttemptOutcomes.Succeeded ? null : result.Failure?.ToString(),
            cancellationToken);
    }

    private Task<LogicalCheckExecutionStatus> CompleteIneligibleAsync(
        SafeHttpTransportRequest request,
        ExecutionLeaseClaim claim,
        ExecutionAttempt attempt,
        CancellationToken cancellationToken) =>
        PersistResultAsync(
            request,
            Failure(request, SafeHttpFailureKind.Cancelled),
            claim,
            attempt,
            ExecutionAttemptOutcomes.Cancelled,
            "TargetIneligible",
            cancellationToken);

    private Task<LogicalCheckExecutionStatus> CompleteExhaustedAsync(
        SafeHttpTransportRequest request,
        ExecutionLeaseClaim claim,
        ExecutionAttempt attempt,
        CancellationToken cancellationToken) =>
        PersistResultAsync(
            request,
            Failure(request, SafeHttpFailureKind.ExecutionExhausted),
            claim,
            attempt,
            ExecutionAttemptOutcomes.TerminalFailure,
            "RetriesExhausted",
            cancellationToken);

    private async Task<LogicalCheckExecutionStatus> PersistResultAsync(
        SafeHttpTransportRequest request,
        SafeHttpTransportResult result,
        ExecutionLeaseClaim claim,
        ExecutionAttempt attempt,
        string attemptOutcome,
        string? failureCategory,
        CancellationToken cancellationToken)
    {
        var status = await historyService.RecordAsync(new(
            claim,
            request,
            result,
            new(attempt.Id, attemptOutcome, failureCategory)), cancellationToken);
        return status switch
        {
            HttpCheckHistoryWriteStatus.Recorded => LogicalCheckExecutionStatus.Completed,
            HttpCheckHistoryWriteStatus.AlreadyRecorded => LogicalCheckExecutionStatus.AlreadyCompleted,
            HttpCheckHistoryWriteStatus.LeaseLost => LogicalCheckExecutionStatus.RetryRequired,
            _ => throw new InvalidOperationException($"Logical check finalization failed with {status}.")
        };
    }

    private async Task<LogicalCheckExecutionStatus> PrepareRetryAsync(
        LogicalCheck check,
        ExecutionLeaseClaim claim,
        ExecutionAttempt attempt,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        attempt.InfrastructureOutcome = ExecutionAttemptOutcomes.RetryableFailure;
        attempt.FailureCategory = "Infrastructure";
        attempt.FinishedAt = now;
        foreach (var work in check.DurableWork)
        {
            work.State = DurableWorkStates.Enqueued;
            work.LastFailureCategory = "Infrastructure";
            work.LastFailureAt = now;
            work.UpdatedAt = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await leaseService.ReleaseAsync(claim, cancellationToken);
        return LogicalCheckExecutionStatus.RetryRequired;
    }

    private async Task<int> NextAttemptNumberAsync(
        Guid logicalCheckId,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.ExecutionAttempts
            .Where(attempt => attempt.LogicalCheckId == logicalCheckId)
            .Select(attempt => attempt.AttemptNumber)
            .DefaultIfEmpty()
            .MaxAsync(cancellationToken);
        return current + 1;
    }

    private static SafeHttpTransportRequest CreateRequest(LogicalCheck check)
    {
        var snapshot = check.ConfigurationSnapshot;
        var endpoint = check.EndpointMonitor.Endpoint;
        return new(
            endpoint.Id,
            endpoint.NormalizedUrl,
            endpoint.Environment.IsProduction,
            snapshot.MaxRedirects,
            snapshot.MaxResponseBodyBytes,
            snapshot.TimeoutSeconds);
    }

    private static SafeHttpTransportResult Failure(
        SafeHttpTransportRequest request,
        SafeHttpFailureKind failure) => new(
            failure,
            null,
            null,
            TimeSpan.Zero,
            0,
            false,
            ReadOnlyMemory<byte>.Empty,
            [],
            SafeHttpRequestIdentity.Create(request));

    private static TimeSpan LeaseDuration(int timeoutSeconds) =>
        TimeSpan.FromTicks(Math.Min(
            TimeSpan.FromSeconds(timeoutSeconds).Add(LeaseBuffer).Ticks,
            MaximumLeaseDuration.Ticks));

    private static void MarkWorkProcessing(LogicalCheck check, DateTimeOffset now)
    {
        foreach (var work in check.DurableWork)
        {
            work.State = DurableWorkStates.Processing;
            work.AttemptCount++;
            work.LastFailureCategory = null;
            work.LastFailureAt = null;
            work.UpdatedAt = now;
        }
    }

    private IDisposable? BeginLogScope(LogicalCheck check, ExecuteLogicalCheck command) =>
        logger.BeginScope(new Dictionary<string, object>
        {
            ["LogicalCheckId"] = check.Id,
            ["EndpointId"] = check.EndpointMonitor.EndpointId,
            ["JobId"] = command.JobId
        });

    private static void EnsureExecutable(LogicalCheck check)
    {
        if (check.ConfigurationSnapshot is null
            || check.State is not (LogicalCheckStates.Queued or LogicalCheckStates.Running))
        {
            throw new InvalidOperationException("The logical check is not ready for execution.");
        }
    }

    private static void Validate(ExecuteLogicalCheck command)
    {
        if (command.LogicalCheckId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.JobId)
            || command.JobId.Length > 100
            || string.IsNullOrWhiteSpace(command.WorkerId)
            || command.WorkerId.Length > 100)
        {
            throw new ArgumentException("The logical check execution command is invalid.", nameof(command));
        }
    }
}
