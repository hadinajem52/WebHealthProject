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
    ISslUrgentCheckScheduler urgentCertificateChecks,
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

        var work = FindWork(check, command.DurableWorkId);
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
        AddHistory(check, normalized, command.Evidence, maintenance, now);
        var counterMode = HealthConfirmationEngine.SelectCounterMode(
            check.Source,
            normalized.Outcome,
            normalized.FailureCategory,
            maintenance is not null,
            maintenance?.ContinueFailureCounter ?? false);
        var healthDecision = await ApplyHealthAsync(
            check, normalized, counterMode, now, cancellationToken);
        await incidentAutomation.ApplyAsync(
            check, normalized, healthDecision, counterMode, maintenance is not null, now, cancellationToken,
            // BR-C06: the fingerprint just observed decides which expiry incidents still have a
            // certificate behind them.
            (command.Evidence as SslCertificateEvidence)?.Result.Certificate?.Sha256Fingerprint);
        CompleteAttempt(attempt!, command.Evidence, now);
        CompleteWork(work, now);

        // BR-C07: an urgent certificate check is created in this transaction so it commits with
        // the availability result it came from. Preparing it after the commit would lose it for
        // good whenever the worker died in between — the completed check is never re-executed.
        var urgentCertificateCheck = await urgentCertificateChecks.PrepareAfterTlsFailureAsync(
            check.EndpointMonitor.EndpointId, command.Evidence, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (urgentCertificateCheck is not null)
        {
            // Best-effort hand-off. The work row is already committed, so reconciliation picks
            // it up even if this never runs.
            await urgentCertificateChecks.EnqueueAsync(urgentCertificateCheck, cancellationToken);
        }

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

        var work = FindWork(check, command.DurableWorkId);
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

    private static DurableWork? FindWork(LogicalCheck check, Guid workId) =>
        check.DurableWork.SingleOrDefault(work =>
            work.Id == workId
            && work.WorkKind == MonitorWorkKinds.For(check.ConfigurationSnapshot.MonitorType));

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

        if (evidence is SslCertificateEvidence ssl)
        {
            return ValidateSslEvidence(check, ssl);
        }

        if (evidence is not HttpTransportEvidence http
            || MonitorWorkKinds.IsSsl(check.ConfigurationSnapshot.MonitorType))
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

    private static LogicalCheckFinalizationStatus? ValidateSslEvidence(
        LogicalCheck check,
        SslCertificateEvidence evidence)
    {
        if (!MonitorWorkKinds.IsSsl(check.ConfigurationSnapshot.MonitorType))
        {
            return LogicalCheckFinalizationStatus.InvalidTransportResult;
        }

        var endpoint = check.EndpointMonitor.Endpoint;
        var normalized = EndpointUrlNormalizer.Normalize(evidence.Request.Url);
        if (evidence.Request.EndpointId != endpoint.Id
            || !normalized.Succeeded
            || normalized.NormalizedUrl != endpoint.NormalizedUrl)
        {
            return LogicalCheckFinalizationStatus.TargetMismatch;
        }

        var expected = ExpectedPolicyFingerprint(
            check, ParseAcceptedStatuses(check.ConfigurationSnapshot.AcceptedStatusCodes));
        if (check.PolicyFingerprint != expected
            || check.ConfigurationSnapshot.ConfigurationFingerprint != expected
            || evidence.Request.TimeoutSeconds != check.ConfigurationSnapshot.TimeoutSeconds)
        {
            return LogicalCheckFinalizationStatus.PolicyMismatch;
        }

        // A probe either observed a certificate or reported why it could not; reporting both,
        // or neither, means the result did not come from a completed probe.
        return evidence.Result.Succeeded == (evidence.Result.Certificate is not null)
            ? null
            : LogicalCheckFinalizationStatus.InvalidTransportResult;
    }

    private static NormalizedCheckResult Normalize(
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

        if (evidence is SslCertificateEvidence ssl)
        {
            return SslResultNormalizer.Normalize(new(ssl.Result, now));
        }


        var reason = ((ExecutionTerminalEvidence)evidence).Reason;
        var category = reason == ExecutionTerminalReason.TargetIneligible
            ? HttpFailureCategories.TargetIneligible
            : HttpFailureCategories.ExecutionExhausted;
        var outcome = reason == ExecutionTerminalReason.TargetIneligible
            ? HttpResultOutcomes.Cancelled
            : HttpResultOutcomes.Critical;
        return new(
            outcome, category, null, 0, null, null, null, "WebHealthExecutionV1", now,
            reason == ExecutionTerminalReason.TargetIneligible
                ? "The target is not currently eligible for monitoring."
                : "The execution retry limit was exhausted.",
            [], []);
    }

    /// <summary>
    /// BR-P02. Thresholds come from the check's own configuration snapshot, never from the
    /// monitor as it stands now, so a result is always judged against the thresholds that were
    /// in force when it was measured. A snapshot that recorded no override falls back to the
    /// documented defaults.
    /// </summary>
    private static HttpResultPolicy CreatePolicy(
        CheckConfigurationSnapshot snapshot,
        IReadOnlyCollection<int> statuses) => new(
        statuses,
        snapshot.RequiredContentMarker,
        snapshot.ContentMarkerComparison == "Ordinal",
        snapshot.ProductionHttpSeverity,
        snapshot.MaxResponseBodyBytes,
        new ResponseTimeThresholds(
            snapshot.WarningThresholdMs ?? ResponseTimeThresholds.Default.WarningMs,
            snapshot.CriticalThresholdMs ?? ResponseTimeThresholds.Default.CriticalMs));

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
        var expected = ExpectedPolicyFingerprint(check, statuses);
        return check.PolicyFingerprint == expected
            && snapshot.ConfigurationFingerprint == expected
            && request.TimeoutSeconds == snapshot.TimeoutSeconds
            && request.MaxRedirects == snapshot.MaxRedirects
            && request.MaxResponseBodyBytes == snapshot.MaxResponseBodyBytes;
    }

    private static string ExpectedPolicyFingerprint(
        LogicalCheck check,
        IReadOnlyCollection<int> statuses)
    {
        var snapshot = check.ConfigurationSnapshot;
        return HttpPolicyFingerprint.Create(new(
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
    }

    private static bool IsTransportConsistent(HttpTransportEvidence evidence)
    {
        var result = evidence.Result;
        return result.Redirects.Count <= evidence.Request.MaxRedirects
            && result.TransferredLength is null or >= 0
            && (result.Failure is null || result.TransferredLength is null)
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
        NormalizedCheckResult normalized,
        LogicalCheckTerminalEvidence evidence,
        ActiveMaintenanceOccurrence? maintenance,
        DateTimeOffset completedAt)
    {
        var isSsl = MonitorWorkKinds.IsSsl(check.ConfigurationSnapshot.MonitorType);
        dbContext.CheckResults.Add(new CheckResult
        {
            LogicalCheckId = check.Id,
            EndpointMonitorId = check.EndpointMonitorId,
            Outcome = normalized.Outcome,
            FailureCategory = normalized.FailureCategory,
            HttpStatus = normalized.HttpStatus,
            DnsDurationMs = normalized.Timing?.DnsDurationMs,
            ConnectDurationMs = normalized.Timing?.ConnectDurationMs,
            TlsDurationMs = normalized.Timing?.TlsDurationMs,
            TtfbDurationMs = normalized.Timing?.TtfbDurationMs,
            TotalDurationMs = normalized.TotalDurationMs,
            TransferredLength = normalized.TransferredLength,
            DecodedLength = normalized.DecodedLength,
            LengthSource = normalized.LengthSource,
            ResponseTruncated = IsResponseTruncated(evidence),
            MonitorSource = normalized.MonitorSource,
            MeasuredAt = normalized.MeasuredAt,
            MaintenanceOccurrenceId = maintenance?.OccurrenceId,
            IsMaintenance = maintenance is not null,
            // Uptime is an availability measure (BR-U03, BR-U05). A certificate check says
            // nothing about whether the site was reachable, so it never becomes an uptime
            // sample even when it succeeds.
            CountsForUptime = !isSsl
                && check.Source == LogicalCheckSources.Scheduled
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
        if (evidence is SslCertificateEvidence { Result.Certificate: { } certificate })
        {
            dbContext.CertificateObservations.Add(new CertificateObservation
            {
                LogicalCheckId = check.Id,
                EndpointMonitorId = check.EndpointMonitorId,
                Subject = certificate.Subject,
                Issuer = certificate.Issuer,
                SerialNumber = certificate.SerialNumber,
                Sha256Fingerprint = certificate.Sha256Fingerprint,
                NotBefore = certificate.NotBefore,
                NotAfter = certificate.NotAfter,
                // The same instant the expiry finding was judged at (BR-C04), so the stored
                // day count can never disagree with the severity that was raised from it.
                DaysRemaining = CertificateExpiry.DaysRemaining(
                    certificate.NotAfter, normalized.MeasuredAt),
                ValidationCategory = certificate.ValidationCategory.ToString(),
                HostnameMatched = certificate.HostnameMatched,
                ChainTrusted = certificate.ChainTrusted,
                SubjectAlternativeNames = FormatAlternativeNames(certificate.SubjectAlternativeNames),
                ObservedAt = certificate.ObservedAt
            });
        }

        check.State = LogicalCheckStates.Completed;
        check.CompletedAt = completedAt;
    }

    private static string? FormatAlternativeNames(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return null;
        }

        var joined = string.Join(", ", names);
        return joined.Length <= 1024 ? joined : joined[..1024];
    }

    private async Task<HealthConfirmationDecision> ApplyHealthAsync(
        LogicalCheck check,
        NormalizedCheckResult result,
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
            CheckResultIssues.Observe(result, check.ConfigurationSnapshot.FailureConfirmationCount),
            result.Outcome == HttpResultOutcomes.Healthy,
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
            SslCertificateEvidence { Result.Failure: SslProbeFailureKind.Cancelled } =>
                ExecutionAttemptOutcomes.Cancelled,
            _ => ExecutionAttemptOutcomes.Succeeded
        };
        attempt.FailureCategory = evidence switch
        {
            ExecutionTerminalEvidence terminal => terminal.Reason.ToString(),
            HttpTransportEvidence { Result.Failure: { } failure } => failure.ToString(),
            SslCertificateEvidence { Result.Failure: { } probeFailure } => probeFailure.ToString(),
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
