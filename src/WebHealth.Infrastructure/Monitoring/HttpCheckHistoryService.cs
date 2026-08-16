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

        var now = timeProvider.GetUtcNow();
        if (!await TryConsumeLeaseAsync(request.Lease, cancellationToken))
        {
            return HttpCheckHistoryWriteStatus.LeaseLost;
        }

        var policy = CreatePolicy(logicalCheck.ConfigurationSnapshot);
        if (logicalCheck.PolicyFingerprint != logicalCheck.ConfigurationSnapshot.ConfigurationFingerprint
            || request.Request.MaxRedirects != logicalCheck.ConfigurationSnapshot.MaxRedirects
            || request.Request.MaxResponseBodyBytes != policy.MaxResponseBodyBytes
            || !IsTransportConsistent(request))
        {
            return HttpCheckHistoryWriteStatus.TargetMismatch;
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

    private static HttpResultPolicy CreatePolicy(CheckConfigurationSnapshot snapshot) => new(
        ParseAcceptedStatuses(snapshot.AcceptedStatusCodes),
        snapshot.RequiredContentMarker,
        snapshot.ContentMarkerComparison == "Ordinal",
        snapshot.ProductionHttpSeverity,
        snapshot.MaxResponseBodyBytes);

    private static bool IsTransportConsistent(RecordHttpCheckHistory request)
    {
        var transport = request.Transport;
        if (transport.Redirects.Count > request.Request.MaxRedirects
            || transport.Body.Length > request.Request.MaxResponseBodyBytes
            || transport.ResponseBytesRead < transport.Body.Length
            || transport.Redirects.Any(hop =>
                hop.StatusCode is < 300 or > 399
                || hop.FromUrl.Length > 2048
                || hop.ToUrl.Length > 2048
                || hop.FromUrl.Contains('?')
                || hop.ToUrl.Contains('?')))
        {
            return false;
        }

        return transport.BodyTruncated
            ? transport.Body.Length == request.Request.MaxResponseBodyBytes
              && transport.ResponseBytesRead == request.Request.MaxResponseBodyBytes + 1L
            : transport.ResponseBytesRead == transport.Body.Length;
    }

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
