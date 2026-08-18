using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Health;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Maintenance;
using WebHealth.Domain.Health;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Health;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class LogicalCheckFinalizationService(
    ApplicationDbContext dbContext,
    IMaintenanceEvaluator maintenanceEvaluator,
    IncidentAutomationService incidentAutomation,
    TimeProvider timeProvider) : ILogicalCheckFinalizationService
{
    public async Task<LogicalCheckFinalizationStatus> FinalizeAsync(
        FinalizeLogicalCheck command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await LockLogicalCheckAsync(command.Lease.LogicalCheckId, cancellationToken);
        var check = await LoadLogicalCheckAsync(command.Lease.LogicalCheckId, cancellationToken);
        if (check is null)
        {
            return LogicalCheckFinalizationStatus.InvalidLogicalCheck;
        }

        var attempt = check.Attempts.SingleOrDefault(candidate => candidate.Id == command.AttemptId);
        if (check.Result is not null)
        {
            await CompleteAsSupersededAsync(attempt, transaction, cancellationToken);
            return LogicalCheckFinalizationStatus.AlreadyFinalized;
        }

        if (check.EndpointMonitorId != command.Lease.EndpointMonitorId
            || check.State != LogicalCheckStates.Running)
        {
            return LogicalCheckFinalizationStatus.InvalidLogicalCheck;
        }

        if (!IsRunning(attempt))
        {
            return LogicalCheckFinalizationStatus.InvalidExecutionAttempt;
        }

        var work = FindHttpWork(check, command.DurableWorkId);
        if (work is null)
        {
            return LogicalCheckFinalizationStatus.InvalidDurableWork;
        }

        var evidenceStatus = ValidateEvidence(check, command.Evidence);
        if (evidenceStatus is not null)
        {
            return evidenceStatus.Value;
        }

        if (!await TryConsumeLeaseAsync(command.Lease, cancellationToken))
        {
            if (await HasNewerLeaseGenerationAsync(command.Lease, cancellationToken))
            {
                await CompleteAsSupersededAsync(attempt, transaction, cancellationToken);
            }
            return LogicalCheckFinalizationStatus.LeaseLost;
        }

        var now = timeProvider.GetUtcNow();
        var normalized = Normalize(check, command.Evidence, now);
        var maintenance = await maintenanceEvaluator.FindActiveAsync(check.EndpointMonitorId, normalized.MeasuredAt, cancellationToken);
        AddHistory(check, normalized, IsResponseTruncated(command.Evidence), maintenance, now);
        var counterMode = HealthConfirmationEngine.SelectCounterMode(
            check.Source,
            normalized.Outcome,
            normalized.FailureCategory,
            maintenance is not null,
            maintenance?.ContinueFailureCounter ?? false);
        var healthDecision = await ApplyHealthAsync(
            check, normalized, counterMode, now, cancellationToken);
        await incidentAutomation.ApplyAsync(
            check, normalized, healthDecision, counterMode, maintenance is not null, now, cancellationToken);
        CompleteAttempt(attempt!, command.Evidence, now);
        CompleteWork(work, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return LogicalCheckFinalizationStatus.Finalized;
    }

    public async Task<LogicalCheckRetryStatus> PrepareRetryAsync(
        PrepareLogicalCheckRetry command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await LockLogicalCheckAsync(command.Lease.LogicalCheckId, cancellationToken);
        var check = await LoadLogicalCheckAsync(command.Lease.LogicalCheckId, cancellationToken);
        if (check is null)
        {
            return LogicalCheckRetryStatus.InvalidExecutionAttempt;
        }

        var attempt = check.Attempts.SingleOrDefault(candidate => candidate.Id == command.AttemptId);
        if (check.Result is not null)
        {
            await CompleteAsSupersededAsync(attempt, transaction, cancellationToken);
            return LogicalCheckRetryStatus.AlreadyFinalized;
        }

        if (!IsRunning(attempt))
        {
            return LogicalCheckRetryStatus.InvalidExecutionAttempt;
        }

        var work = FindHttpWork(check, command.DurableWorkId);
        if (work is null)
        {
            return LogicalCheckRetryStatus.InvalidDurableWork;
        }

        if (!await TryConsumeLeaseAsync(command.Lease, cancellationToken))
        {
            if (!await HasNewerLeaseGenerationAsync(command.Lease, cancellationToken))
            {
                return LogicalCheckRetryStatus.LeaseLost;
            }
            await CompleteAsSupersededAsync(attempt, transaction, cancellationToken);
            return LogicalCheckRetryStatus.Superseded;
        }

        var now = timeProvider.GetUtcNow();
        attempt!.InfrastructureOutcome = ExecutionAttemptOutcomes.RetryableFailure;
        attempt.FailureCategory = Bounded(command.FailureCategory);
        attempt.FinishedAt = now;
        work.State = DurableWorkStates.Enqueued;
        work.LastFailureCategory = Bounded(command.FailureCategory);
        work.LastFailureAt = now;
        work.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return LogicalCheckRetryStatus.RetryPrepared;
    }

    private Task<LogicalCheck?> LoadLogicalCheckAsync(Guid logicalCheckId, CancellationToken token) =>
        dbContext.LogicalChecks
            .Include(check => check.ConfigurationSnapshot)
            .Include(check => check.EndpointMonitor)
                .ThenInclude(monitor => monitor.Endpoint)
                    .ThenInclude(endpoint => endpoint.Environment)
                        .ThenInclude(environment => environment.Website)
                            .ThenInclude(website => website.Client)
            .Include(check => check.Result)
            .Include(check => check.Attempts)
            .Include(check => check.DurableWork)
            .SingleOrDefaultAsync(check => check.Id == logicalCheckId, token);

    private static DurableWork? FindHttpWork(LogicalCheck check, Guid workId) =>
        check.DurableWork.SingleOrDefault(work =>
            work.Id == workId && work.WorkKind == DurableWorkKinds.HttpCheck);

    private static bool IsRunning(ExecutionAttempt? attempt) => attempt is
    {
        FinishedAt: null,
        InfrastructureOutcome: ExecutionAttemptOutcomes.Running
    };

    private static LogicalCheckFinalizationStatus? ValidateEvidence(
        LogicalCheck check,
        LogicalCheckTerminalEvidence evidence)
    {
        if (evidence is ExecutionTerminalEvidence terminal)
        {
            return terminal.Reason is ExecutionTerminalReason.TargetIneligible
                or ExecutionTerminalReason.RetriesExhausted
                ? null
                : LogicalCheckFinalizationStatus.InvalidTransportResult;
        }

        if (evidence is not HttpTransportEvidence http)
        {
            return LogicalCheckFinalizationStatus.InvalidTransportResult;
        }

        if (!MatchesTarget(check, http.Request)
            || http.Result.RequestIdentity != SafeHttpRequestIdentity.Create(http.Request))
        {
            return LogicalCheckFinalizationStatus.TargetMismatch;
        }

        var statuses = ParseAcceptedStatuses(check.ConfigurationSnapshot.AcceptedStatusCodes);
        if (!MatchesPolicy(check, http.Request, statuses))
        {
            return LogicalCheckFinalizationStatus.PolicyMismatch;
        }

        return IsTransportConsistent(http)
            ? null
            : LogicalCheckFinalizationStatus.InvalidTransportResult;
    }

    private static NormalizedHttpResult Normalize(
        LogicalCheck check,
        LogicalCheckTerminalEvidence evidence,
        DateTimeOffset now)
    {
        if (evidence is HttpTransportEvidence http)
        {
            var statuses = ParseAcceptedStatuses(check.ConfigurationSnapshot.AcceptedStatusCodes);
            return HttpResultNormalizer.Normalize(new(
                http.Request,
                http.Result,
                CreatePolicy(check.ConfigurationSnapshot, statuses),
                now));
        }

        var reason = ((ExecutionTerminalEvidence)evidence).Reason;
        var category = reason == ExecutionTerminalReason.TargetIneligible
            ? HttpFailureCategories.TargetIneligible
            : HttpFailureCategories.ExecutionExhausted;
        var outcome = reason == ExecutionTerminalReason.TargetIneligible
            ? HttpResultOutcomes.Cancelled
            : HttpResultOutcomes.Critical;
        return new(
            outcome, category, null, 0, null, null, "WebHealthExecutionV1", now,
            reason == ExecutionTerminalReason.TargetIneligible
                ? "The target is not currently eligible for monitoring."
                : "The execution retry limit was exhausted.",
            [], []);
    }

    private static HttpResultPolicy CreatePolicy(
        CheckConfigurationSnapshot snapshot,
        IReadOnlyCollection<int> statuses) => new(
        statuses,
        snapshot.RequiredContentMarker,
        snapshot.ContentMarkerComparison == "Ordinal",
        snapshot.ProductionHttpSeverity,
        snapshot.MaxResponseBodyBytes);

    private static bool MatchesTarget(LogicalCheck check, SafeHttpTransportRequest request)
    {
        var endpoint = check.EndpointMonitor.Endpoint;
        var normalized = EndpointUrlNormalizer.Normalize(request.Url);
        return request.EndpointId == endpoint.Id
            && request.IsProduction == endpoint.Environment.IsProduction
            && normalized.Succeeded
            && normalized.NormalizedUrl == endpoint.NormalizedUrl;
    }

    private static bool MatchesPolicy(
        LogicalCheck check,
        SafeHttpTransportRequest request,
        IReadOnlyCollection<int> statuses)
    {
        var snapshot = check.ConfigurationSnapshot;
        var expected = HttpPolicyFingerprint.Create(new(
            check.EndpointMonitor.Endpoint.NormalizedUrl,
            snapshot.MonitorType,
            check.EndpointMonitor.Endpoint.Environment.IsProduction,
            snapshot.IntervalSeconds,
            snapshot.TimeoutSeconds,
            snapshot.FailureConfirmationCount,
            snapshot.RecoveryConfirmationCount,
            snapshot.WarningThresholdMs,
            snapshot.CriticalThresholdMs,
            statuses,
            snapshot.RequiredContentMarker,
            snapshot.ContentMarkerComparison,
            snapshot.ProductionHttpSeverity,
            snapshot.MaxResponseBodyBytes,
            snapshot.MaxRedirects));
        return check.PolicyFingerprint == expected
            && snapshot.ConfigurationFingerprint == expected
            && request.TimeoutSeconds == snapshot.TimeoutSeconds
            && request.MaxRedirects == snapshot.MaxRedirects
            && request.MaxResponseBodyBytes == snapshot.MaxResponseBodyBytes;
    }

    private static bool IsTransportConsistent(HttpTransportEvidence evidence)
    {
        var result = evidence.Result;
        return result.Redirects.Count <= evidence.Request.MaxRedirects
            && result.Body.Length <= evidence.Request.MaxResponseBodyBytes
            && result.ResponseBytesRead >= result.Body.Length
            && HasValidBodyEvidence(evidence)
            && HasValidRedirectChain(evidence)
            && (result.Failure is null
                || result is { Body.Length: 0, ResponseBytesRead: 0, BodyTruncated: false });
    }

    private static bool HasValidBodyEvidence(HttpTransportEvidence evidence) =>
        evidence.Result.BodyTruncated
            ? evidence.Result.Body.Length == evidence.Request.MaxResponseBodyBytes
              && evidence.Result.ResponseBytesRead == evidence.Request.MaxResponseBodyBytes + 1L
            : evidence.Result.ResponseBytesRead == evidence.Result.Body.Length;

    private static bool HasValidRedirectChain(HttpTransportEvidence evidence)
    {
        var initial = EndpointUrlNormalizer.Normalize(evidence.Request.Url);
        if (!initial.Succeeded)
        {
            return false;
        }

        var currentUrl = RedactedUrl(initial.NormalizedUrl!);
        for (var index = 0; index < evidence.Result.Redirects.Count; index++)
        {
            var hop = evidence.Result.Redirects[index];
            if (hop.StatusCode is < 300 or > 399
                || !IsSafeNormalizedUrl(hop.FromUrl)
                || !IsSafeNormalizedUrl(hop.ToUrl)
                || hop.FromUrl != currentUrl
                || hop.IsLoop != (evidence.Result.Failure == SafeHttpFailureKind.RedirectLoop
                    && index == evidence.Result.Redirects.Count - 1))
            {
                return false;
            }

            currentUrl = hop.ToUrl;
        }

        return !(evidence.Result.Failure == SafeHttpFailureKind.RedirectLoop
                && evidence.Result.Redirects.Count == 0)
            && (evidence.Result.FinalDestination is null
                || IsSafeNormalizedUrl(evidence.Result.FinalDestination.Url)
                && evidence.Result.FinalDestination.Url == currentUrl);
    }

    private static bool IsSafeNormalizedUrl(string url)
    {
        if (url.Length > 2048 || url.Contains('?'))
        {
            return false;
        }

        var normalized = EndpointUrlNormalizer.Normalize(url);
        return normalized.Succeeded && RedactedUrl(normalized.NormalizedUrl!) == url;
    }

    private static string RedactedUrl(string url) =>
        new Uri(url, UriKind.Absolute).GetLeftPart(UriPartial.Path);

    private static IReadOnlyCollection<int> ParseAcceptedStatuses(string value)
    {
        if (value.Length == 0)
        {
            return [];
        }

        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Any(item => !int.TryParse(item, out var code) || code is < 100 or > 599))
        {
            throw new InvalidOperationException("The check snapshot contains invalid accepted HTTP statuses.");
        }

        return values.Select(int.Parse).Distinct().ToArray();
    }

    private void AddHistory(
        LogicalCheck check,
        NormalizedHttpResult normalized,
        bool responseTruncated,
        ActiveMaintenanceOccurrence? maintenance,
        DateTimeOffset completedAt)
    {
        dbContext.CheckResults.Add(new CheckResult
        {
            LogicalCheckId = check.Id,
            Outcome = normalized.Outcome,
            FailureCategory = normalized.FailureCategory,
            HttpStatus = normalized.HttpStatus,
            DnsDurationMs = normalized.Timing?.DnsDurationMs,
            ConnectDurationMs = normalized.Timing?.ConnectDurationMs,
            TlsDurationMs = normalized.Timing?.TlsDurationMs,
            TtfbDurationMs = normalized.Timing?.TtfbDurationMs,
            TotalDurationMs = normalized.TotalDurationMs,
            DecodedLength = normalized.DecodedLength,
            LengthSource = normalized.LengthSource,
            ResponseTruncated = responseTruncated,
            MonitorSource = normalized.MonitorSource,
            MeasuredAt = normalized.MeasuredAt,
            MaintenanceOccurrenceId = maintenance?.OccurrenceId,
            IsMaintenance = maintenance is not null,
            CountsForUptime = check.Source == LogicalCheckSources.Scheduled
                && normalized.Outcome != HttpResultOutcomes.Cancelled
                && maintenance is null,
            SafeDiagnostic = normalized.SafeDiagnostic,
            CompletedAt = completedAt
        });
        dbContext.RedirectHops.AddRange(normalized.Redirects.Select(hop => new RedirectHop
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = check.Id,
            HopNumber = hop.HopNumber,
            NormalizedFromUrl = hop.FromUrl,
            NormalizedToUrl = hop.ToUrl,
            HttpStatus = hop.HttpStatus,
            IsLoop = hop.IsLoop
        }));
        dbContext.Findings.AddRange(normalized.Findings.Select(finding => new Finding
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = check.Id,
            RuleKey = finding.RuleKey,
            Severity = finding.Severity,
            ObservedValue = finding.ObservedValue,
            ExpectedValue = finding.ExpectedValue,
            IssueKey = finding.IssueKey
        }));
        check.State = LogicalCheckStates.Completed;
        check.CompletedAt = completedAt;
    }

    private async Task<HealthConfirmationDecision> ApplyHealthAsync(
        LogicalCheck check,
        NormalizedHttpResult result,
        HealthCounterMode counterMode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await LockEndpointMonitorAsync(check.EndpointMonitorId, cancellationToken);
        var states = await dbContext.IssueStates
            .FromSqlInterpolated($"""
                SELECT * FROM web_health.issue_state
                WHERE endpoint_monitor_id = {check.EndpointMonitorId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        var health = await dbContext.EndpointHealth.SingleOrDefaultAsync(
            candidate => candidate.EndpointMonitorId == check.EndpointMonitorId,
            cancellationToken);
        var decision = HealthConfirmationEngine.Evaluate(new(
            health?.ConfirmedStatus ?? EndpointHealthStatuses.Unknown,
            states.Select(ToCounter).ToArray(),
            ObservedIssueKeys(result),
            result.Outcome == HttpResultOutcomes.Healthy,
            check.ConfigurationSnapshot.FailureConfirmationCount,
            check.ConfigurationSnapshot.RecoveryConfirmationCount,
            counterMode));

        ApplyIssueCounters(check.EndpointMonitorId, states, decision.Issues, now);
        ApplyConfirmedHealth(check, health, decision.ConfirmedStatus, now);
        return decision;
    }

    private void ApplyIssueCounters(
        Guid endpointMonitorId,
        IReadOnlyCollection<IssueState> currentStates,
        IReadOnlyCollection<HealthIssueCounter> decisions,
        DateTimeOffset now)
    {
        var current = currentStates.ToDictionary(state => state.IssueKey, StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            if (!current.TryGetValue(decision.IssueKey, out var state))
            {
                dbContext.IssueStates.Add(new IssueState
                {
                    Id = Guid.NewGuid(),
                    EndpointMonitorId = endpointMonitorId,
                    IssueKey = decision.IssueKey,
                    ConsecutiveFailures = decision.ConsecutiveFailures,
                    ConsecutiveRecoveries = decision.ConsecutiveRecoveries,
                    UpdatedAt = now,
                    Version = 1
                });
                continue;
            }

            if (state.ConsecutiveFailures == decision.ConsecutiveFailures
                && state.ConsecutiveRecoveries == decision.ConsecutiveRecoveries)
            {
                continue;
            }

            state.ConsecutiveFailures = decision.ConsecutiveFailures;
            state.ConsecutiveRecoveries = decision.ConsecutiveRecoveries;
            state.UpdatedAt = now;
            state.Version++;
        }
    }

    private void ApplyConfirmedHealth(
        LogicalCheck check,
        EndpointHealth? health,
        string? confirmedStatus,
        DateTimeOffset now)
    {
        if (confirmedStatus is null || health?.ConfirmedStatus == confirmedStatus)
        {
            return;
        }

        if (health is null)
        {
            dbContext.EndpointHealth.Add(new EndpointHealth
            {
                EndpointMonitorId = check.EndpointMonitorId,
                EvidenceLogicalCheckId = check.Id,
                ConfirmedStatus = confirmedStatus,
                ConfirmedAt = now,
                Version = 1
            });
            return;
        }

        health.ConfirmedStatus = confirmedStatus;
        health.EvidenceLogicalCheckId = check.Id;
        health.ConfirmedAt = now;
        health.Version++;
    }

    private static HealthIssueCounter ToCounter(IssueState state) => new(
        state.IssueKey,
        state.ConsecutiveFailures,
        state.ConsecutiveRecoveries);

    private static IReadOnlyCollection<string> ObservedIssueKeys(NormalizedHttpResult result)
    {
        if (result.Outcome == HttpResultOutcomes.Healthy)
        {
            return [];
        }

        var issueKeys = result.Findings.Select(finding => finding.IssueKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return issueKeys.Length > 0
            ? issueKeys
            : [HttpIssueIdentity.Create($"Http.{result.FailureCategory ?? "Unknown"}")];
    }

    private static void CompleteAttempt(
        ExecutionAttempt attempt,
        LogicalCheckTerminalEvidence evidence,
        DateTimeOffset now)
    {
        attempt.InfrastructureOutcome = evidence switch
        {
            ExecutionTerminalEvidence { Reason: ExecutionTerminalReason.TargetIneligible } =>
                ExecutionAttemptOutcomes.Cancelled,
            ExecutionTerminalEvidence => ExecutionAttemptOutcomes.TerminalFailure,
            HttpTransportEvidence { Result.Failure: SafeHttpFailureKind.Cancelled } =>
                ExecutionAttemptOutcomes.Cancelled,
            _ => ExecutionAttemptOutcomes.Succeeded
        };
        attempt.FailureCategory = evidence switch
        {
            ExecutionTerminalEvidence terminal => terminal.Reason.ToString(),
            HttpTransportEvidence { Result.Failure: { } failure } => failure.ToString(),
            _ => null
        };
        attempt.FinishedAt = now;
    }

    private static void CompleteWork(DurableWork work, DateTimeOffset now)
    {
        work.State = DurableWorkStates.Completed;
        work.LeaseOwnerToken = null;
        work.LeaseAcquiredAt = null;
        work.LeaseExpiresAt = null;
        work.UpdatedAt = now;
    }

    private async Task CompleteAsSupersededAsync(
        ExecutionAttempt? attempt,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (IsRunning(attempt))
        {
            attempt!.InfrastructureOutcome = ExecutionAttemptOutcomes.Superseded;
            attempt.FailureCategory = "LeaseSuperseded";
            attempt.FinishedAt = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> TryConsumeLeaseAsync(
        ExecutionLeaseClaim claim,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE web_health.execution_lease
            SET expires_at = GREATEST(clock_timestamp(), acquired_at + interval '1 microsecond')
            WHERE endpoint_monitor_id = @endpoint_monitor_id
              AND logical_check_id = @logical_check_id
              AND owner_token = @owner_token
              AND fencing_generation = @fencing_generation
              AND expires_at > clock_timestamp();
            """;
        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("endpoint_monitor_id", NpgsqlDbType.Uuid, claim.EndpointMonitorId);
        command.Parameters.AddWithValue("logical_check_id", NpgsqlDbType.Uuid, claim.LogicalCheckId);
        command.Parameters.AddWithValue("owner_token", NpgsqlDbType.Uuid, claim.OwnerToken);
        command.Parameters.AddWithValue("fencing_generation", NpgsqlDbType.Bigint, claim.FencingGeneration);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private Task<bool> HasNewerLeaseGenerationAsync(
        ExecutionLeaseClaim claim,
        CancellationToken cancellationToken) =>
        dbContext.ExecutionLeases.AsNoTracking().AnyAsync(lease =>
            lease.EndpointMonitorId == claim.EndpointMonitorId
            && lease.LogicalCheckId == claim.LogicalCheckId
            && lease.FencingGeneration > claim.FencingGeneration,
            cancellationToken);

    private async Task LockLogicalCheckAsync(Guid logicalCheckId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            "SELECT id FROM web_health.logical_check WHERE id = @id FOR UPDATE");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, logicalCheckId);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task LockEndpointMonitorAsync(Guid endpointMonitorId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            "SELECT id FROM web_health.endpoint_monitor WHERE id = @id FOR UPDATE");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, endpointMonitorId);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private NpgsqlCommand CreateCommand(string sql) => new(
        sql,
        (NpgsqlConnection)dbContext.Database.GetDbConnection(),
        dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);

    private Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken token) =>
        dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);

    private static bool IsResponseTruncated(LogicalCheckTerminalEvidence evidence) =>
        evidence is HttpTransportEvidence { Result.BodyTruncated: true };

    private static string Bounded(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? "Infrastructure" : trimmed[..Math.Min(trimmed.Length, 100)];
    }
}
