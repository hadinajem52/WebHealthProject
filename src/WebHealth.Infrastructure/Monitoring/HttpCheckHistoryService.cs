using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class HttpCheckHistoryService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : IHttpCheckHistoryService
{
    public async Task<HttpCheckHistoryWriteStatus> RecordAsync(
        RecordHttpCheckHistory request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        await LockLogicalCheckAsync(request.Lease.LogicalCheckId, cancellationToken);
        var logicalCheck = await LoadLogicalCheckAsync(request.Lease.LogicalCheckId, cancellationToken);
        if (logicalCheck is null)
        {
            return HttpCheckHistoryWriteStatus.InvalidLogicalCheck;
        }

        if (await dbContext.CheckResults.AnyAsync(
            result => result.LogicalCheckId == request.Lease.LogicalCheckId,
            cancellationToken))
        {
            return HttpCheckHistoryWriteStatus.AlreadyRecorded;
        }

        if (logicalCheck.EndpointMonitorId != request.Lease.EndpointMonitorId
            || logicalCheck.State != LogicalCheckStates.Running)
        {
            return HttpCheckHistoryWriteStatus.InvalidLogicalCheck;
        }

        if (!MatchesTarget(logicalCheck, request.Request))
        {
            return HttpCheckHistoryWriteStatus.TargetMismatch;
        }

        var requestIdentity = SafeHttpRequestIdentity.Create(request.Request);
        if (requestIdentity is null || request.Transport.RequestIdentity != requestIdentity)
        {
            return HttpCheckHistoryWriteStatus.TargetMismatch;
        }

        var now = timeProvider.GetUtcNow();
        if (!await TryConsumeLeaseAsync(request.Lease, cancellationToken))
        {
            return HttpCheckHistoryWriteStatus.LeaseLost;
        }

        var snapshot = logicalCheck.ConfigurationSnapshot;
        var acceptedStatuses = ParseAcceptedStatuses(snapshot.AcceptedStatusCodes);
        var policy = CreatePolicy(snapshot, acceptedStatuses);
        if (!MatchesPolicy(logicalCheck, request.Request, acceptedStatuses))
        {
            return HttpCheckHistoryWriteStatus.PolicyMismatch;
        }

        if (!IsTransportConsistent(request))
        {
            return HttpCheckHistoryWriteStatus.InvalidTransportResult;
        }

        var normalized = HttpResultNormalizer.Normalize(new(
            request.Request,
            request.Transport,
            policy,
            now));
        AddHistory(logicalCheck, normalized, request.Transport.BodyTruncated, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return HttpCheckHistoryWriteStatus.Recorded;
    }

    private Task<LogicalCheck?> LoadLogicalCheckAsync(Guid logicalCheckId, CancellationToken cancellationToken) =>
        dbContext.LogicalChecks
            .Include(check => check.ConfigurationSnapshot)
            .Include(check => check.EndpointMonitor)
                .ThenInclude(monitor => monitor.Endpoint)
                    .ThenInclude(endpoint => endpoint.Environment)
            .SingleOrDefaultAsync(check => check.Id == logicalCheckId, cancellationToken);

    private async Task LockLogicalCheckAsync(Guid logicalCheckId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id FROM web_health.logical_check WHERE id = @id FOR UPDATE",
            (NpgsqlConnection)dbContext.Database.GetDbConnection(),
            CurrentTransaction());
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, logicalCheckId);
        await command.ExecuteScalarAsync(cancellationToken);
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
        await using var command = new NpgsqlCommand(
            sql,
            (NpgsqlConnection)dbContext.Database.GetDbConnection(),
            CurrentTransaction());
        command.Parameters.AddWithValue("endpoint_monitor_id", NpgsqlDbType.Uuid, claim.EndpointMonitorId);
        command.Parameters.AddWithValue("logical_check_id", NpgsqlDbType.Uuid, claim.LogicalCheckId);
        command.Parameters.AddWithValue("owner_token", NpgsqlDbType.Uuid, claim.OwnerToken);
        command.Parameters.AddWithValue("fencing_generation", NpgsqlDbType.Bigint, claim.FencingGeneration);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static bool MatchesTarget(LogicalCheck logicalCheck, SafeHttpTransportRequest request)
    {
        var endpoint = logicalCheck.EndpointMonitor.Endpoint;
        var normalized = EndpointUrlNormalizer.Normalize(request.Url);
        return request.EndpointId == endpoint.Id
            && request.IsProduction == endpoint.Environment.IsProduction
            && normalized.Succeeded
            && normalized.NormalizedUrl == endpoint.NormalizedUrl;
    }

    private static HttpResultPolicy CreatePolicy(
        CheckConfigurationSnapshot snapshot,
        IReadOnlyCollection<int> acceptedStatuses) => new(
        acceptedStatuses,
        snapshot.RequiredContentMarker,
        snapshot.ContentMarkerComparison == "Ordinal",
        snapshot.ProductionHttpSeverity,
        snapshot.MaxResponseBodyBytes);

    private static bool MatchesPolicy(
        LogicalCheck logicalCheck,
        SafeHttpTransportRequest request,
        IReadOnlyCollection<int> acceptedStatuses)
    {
        var snapshot = logicalCheck.ConfigurationSnapshot;
        var expected = HttpPolicyFingerprint.Create(new(
            logicalCheck.EndpointMonitor.Endpoint.NormalizedUrl,
            snapshot.MonitorType,
            logicalCheck.EndpointMonitor.Endpoint.Environment.IsProduction,
            snapshot.IntervalSeconds,
            snapshot.TimeoutSeconds,
            snapshot.FailureConfirmationCount,
            snapshot.RecoveryConfirmationCount,
            snapshot.WarningThresholdMs,
            snapshot.CriticalThresholdMs,
            acceptedStatuses,
            snapshot.RequiredContentMarker,
            snapshot.ContentMarkerComparison,
            snapshot.ProductionHttpSeverity,
            snapshot.MaxResponseBodyBytes,
            snapshot.MaxRedirects));
        return logicalCheck.PolicyFingerprint == expected
            && snapshot.ConfigurationFingerprint == expected
            && request.MaxRedirects == snapshot.MaxRedirects
            && request.MaxResponseBodyBytes == snapshot.MaxResponseBodyBytes;
    }

    private static bool IsTransportConsistent(RecordHttpCheckHistory request)
    {
        var transport = request.Transport;
        if (transport.Redirects.Count > request.Request.MaxRedirects
            || transport.Body.Length > request.Request.MaxResponseBodyBytes
            || transport.ResponseBytesRead < transport.Body.Length
            || !HasValidBodyEvidence(request)
            || !HasValidRedirectChain(request))
        {
            return false;
        }

        return transport.Failure is null
            || transport is { Body.Length: 0, ResponseBytesRead: 0, BodyTruncated: false };
    }

    private static bool HasValidBodyEvidence(RecordHttpCheckHistory request) =>
        request.Transport.BodyTruncated
            ? request.Transport.Body.Length == request.Request.MaxResponseBodyBytes
              && request.Transport.ResponseBytesRead == request.Request.MaxResponseBodyBytes + 1L
            : request.Transport.ResponseBytesRead == request.Transport.Body.Length;

    private static bool HasValidRedirectChain(RecordHttpCheckHistory request)
    {
        var initial = EndpointUrlNormalizer.Normalize(request.Request.Url);
        if (!initial.Succeeded)
        {
            return false;
        }

        var currentUrl = RedactedUrl(initial.NormalizedUrl!);
        for (var index = 0; index < request.Transport.Redirects.Count; index++)
        {
            var hop = request.Transport.Redirects[index];
            if (hop.StatusCode is < 300 or > 399
                || !IsSafeNormalizedUrl(hop.FromUrl)
                || !IsSafeNormalizedUrl(hop.ToUrl)
                || hop.FromUrl != currentUrl
                || hop.IsLoop != (request.Transport.Failure == SafeHttpFailureKind.RedirectLoop
                    && index == request.Transport.Redirects.Count - 1))
            {
                return false;
            }

            currentUrl = hop.ToUrl;
        }

        if (request.Transport.Failure == SafeHttpFailureKind.RedirectLoop
            && request.Transport.Redirects.Count == 0)
        {
            return false;
        }

        return request.Transport.FinalDestination is null
            || MatchesDestination(currentUrl, request.Transport.FinalDestination);
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

    private static bool MatchesDestination(string url, SafeHttpDestination destination)
        => IsSafeNormalizedUrl(destination.Url) && destination.Url == url;

    private static string RedactedUrl(string normalizedUrl) =>
        new Uri(normalizedUrl, UriKind.Absolute).GetLeftPart(UriPartial.Path);

    private static IReadOnlyCollection<int> ParseAcceptedStatuses(string value)
    {
        if (value.Length == 0)
        {
            return [];
        }

        var statuses = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (statuses.Any(status => !int.TryParse(status, out var code) || code is < 100 or > 599))
        {
            throw new InvalidOperationException("The check snapshot contains invalid accepted HTTP statuses.");
        }
        return statuses.Select(int.Parse).Distinct().ToArray();
    }

    private void AddHistory(
        LogicalCheck logicalCheck,
        NormalizedHttpResult normalized,
        bool responseTruncated,
        DateTimeOffset completedAt)
    {
        dbContext.CheckResults.Add(new CheckResult
        {
            LogicalCheckId = logicalCheck.Id,
            Outcome = normalized.Outcome,
            FailureCategory = normalized.FailureCategory,
            HttpStatus = normalized.HttpStatus,
            TotalDurationMs = normalized.TotalDurationMs,
            DecodedLength = normalized.DecodedLength,
            LengthSource = normalized.LengthSource,
            ResponseTruncated = responseTruncated,
            MonitorSource = normalized.MonitorSource,
            MeasuredAt = normalized.MeasuredAt,
            CountsForUptime = logicalCheck.Source == LogicalCheckSources.Scheduled,
            SafeDiagnostic = normalized.SafeDiagnostic,
            CompletedAt = completedAt
        });
        dbContext.RedirectHops.AddRange(normalized.Redirects.Select(hop => new RedirectHop
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = logicalCheck.Id,
            HopNumber = hop.HopNumber,
            NormalizedFromUrl = hop.FromUrl,
            NormalizedToUrl = hop.ToUrl,
            HttpStatus = hop.HttpStatus,
            IsLoop = hop.IsLoop
        }));
        dbContext.Findings.AddRange(normalized.Findings.Select(finding => new Finding
        {
            Id = Guid.NewGuid(),
            LogicalCheckId = logicalCheck.Id,
            RuleKey = finding.RuleKey,
            Severity = finding.Severity,
            ObservedValue = finding.ObservedValue,
            ExpectedValue = finding.ExpectedValue,
            IssueKey = finding.IssueKey
        }));
        logicalCheck.State = LogicalCheckStates.Completed;
        logicalCheck.CompletedAt = completedAt;
    }

    private NpgsqlTransaction? CurrentTransaction() =>
        dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
}
