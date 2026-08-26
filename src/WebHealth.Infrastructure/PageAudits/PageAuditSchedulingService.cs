using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PageAudits;

public sealed class PageAuditSchedulingService(
    ApplicationDbContext dbContext,
    IPageAuditQueue queue,
    ITargetAuthorizationService targetAuthorization,
    PageAuditSchedulingOptions options,
    PageSpeedInsightsOptions providerOptions,
    TimeProvider timeProvider,
    ILogger<PageAuditSchedulingService> logger) : IPageAuditRunner
{
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        var runIds = await OpenDueRunsAsync(cancellationToken);
        return EnqueueAll(runIds);
    }

    public async Task<int> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var staleBefore = now.Subtract(options.ReconciliationDelay);

        var recoverable = await dbContext.PageAuditRuns.AsNoTracking()
            .Where(run =>
                (run.Status == PageAuditRunStatuses.Queued && run.UpdatedAt < staleBefore)
                || (run.Status == PageAuditRunStatuses.Running
                    && run.LeaseExpiresAt != null
                    && run.LeaseExpiresAt < now))
            .OrderBy(run => run.UpdatedAt)
            .ThenBy(run => run.Id)
            .Take(options.ReconciliationBatchSize)
            .Select(run => new { run.Id, run.BatchId, run.AttemptCount })
            .ToArrayAsync(cancellationToken);

        var exhaustedBatchIds = recoverable
            .Where(run => run.AttemptCount >= options.MaximumAttempts)
            .Select(run => run.BatchId)
            .Distinct()
            .ToArray();
        if (exhaustedBatchIds.Length > 0)
        {
            await dbContext.PageAuditRuns
                .Where(run => exhaustedBatchIds.Contains(run.BatchId)
                    && (run.Status == PageAuditRunStatuses.Queued
                        || run.Status == PageAuditRunStatuses.Running))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, PageAuditRunStatuses.Failed)
                    .SetProperty(run => run.FailureCategory,
                        PageAuditFailureCategories.UnknownProviderFailure)
                    .SetProperty(run => run.SafeDiagnostic,
                        "The audit stopped without recording an outcome and has no attempts left.")
                    .SetProperty(run => run.FinishedAt, now)
                    .SetProperty(run => run.LeaseToken, (Guid?)null)
                    .SetProperty(run => run.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(run => run.UpdatedAt, now),
                    cancellationToken);
            logger.LogWarning(
                "Retired {Count} page audit batches that had no attempts left.", exhaustedBatchIds.Length);
        }

        var runIds = recoverable
            .Where(run => !exhaustedBatchIds.Contains(run.BatchId))
            .GroupBy(run => run.BatchId)
            .Select(batch => batch.OrderBy(run => run.Id).First().Id)
            .ToArray();

        if (runIds.Length > 0)
        {
            logger.LogInformation(
                "Re-enqueueing {Count} page audit runs left behind by a lost job or a stopped worker.",
                runIds.Length);
        }

        return EnqueueAll(runIds);
    }

    public async Task<PageAuditManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        var now = timeProvider.GetUtcNow();

        if (!await targetAuthorization.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            return PageAuditManualResult.NotTestable(
                await targetAuthorization.DescribeTestBlockAsync(endpointId, access, cancellationToken));
        }

        var targets = await dbContext.PageAuditTargets.AsNoTracking()
            .Where(candidate => candidate.EndpointId == endpointId
                && candidate.Provider == PageAuditProviders.PageSpeedInsights
                && candidate.IsEnabled)
            .OrderBy(candidate => candidate.Category)
            .ThenBy(candidate => candidate.Strategy)
            .ThenBy(candidate => candidate.Id)
            .ToArrayAsync(cancellationToken);
        if (targets.Length == 0)
        {
            return PageAuditManualResult.Rejected(
                "PageSpeed auditing is not enabled for this endpoint.");
        }

        if (!providerOptions.HasApiKey)
        {
            return PageAuditManualResult.Rejected(
                "No PageSpeed Insights API key is configured, so no audit can be requested.");
        }

        var endpoint = await MonitoringEligibility
            .ApplyTestable(dbContext.Endpoints.AsNoTracking())
            .Where(candidate => candidate.Id == endpointId)
            .Select(candidate => new { candidate.NormalizedUrl })
            .SingleOrDefaultAsync(cancellationToken);
        if (endpoint is null)
        {
            return PageAuditManualResult.Rejected(
                "The endpoint is not active.");
        }

        var eligibility = PageAuditEligibility.Evaluate(endpoint.NormalizedUrl);
        if (!eligibility.IsEligible)
        {
            return PageAuditManualResult.Rejected(DescribeIneligibility(eligibility.Reason));
        }

        var opened = new List<(Guid RunId, Guid BatchId)>();
        var alreadyRunning = 0;
        foreach (var strategyTargets in targets.GroupBy(target => target.Strategy))
        {
            var batchId = Guid.NewGuid();
            foreach (var target in strategyTargets)
            {
                var runId = await OpenManualRunAsync(
                    target, batchId, endpoint.NormalizedUrl, access.UserId, now, cancellationToken);
                if (runId is { } identifier)
                {
                    opened.Add((identifier, batchId));
                }
                else
                {
                    alreadyRunning++;
                }
            }
        }

        if (opened.Count == 0)
        {
            return PageAuditManualResult.Opened(0, alreadyRunning);
        }

        try
        {
            foreach (var runId in opened
                .GroupBy(run => run.BatchId)
                .Select(batch => batch.OrderBy(run => run.RunId).First().RunId))
            {
                queue.Enqueue(runId);
            }
        }
        catch (Exception exception)
        {
            foreach (var runId in opened.Select(run => run.RunId))
            {
                await RetireUnreachableRunAsync(runId, cancellationToken);
            }

            logger.LogError(
                exception,
                "PageAudit runs could not be queued and were retired. EndpointId={EndpointId}",
                endpointId);
            return PageAuditManualResult.Rejected(
                "PageSpeed audits are not running on this instance, so the audit could not be "
                + "started. Enable PageAudits:Scheduling to run them.");
        }

        logger.LogInformation(
            "PageAudit runs queued by request. Count={Count} EndpointId={EndpointId}",
            opened.Count, endpointId);
        return PageAuditManualResult.Opened(opened.Count, alreadyRunning);
    }

    private async Task<Guid?> OpenManualRunAsync(
        PageAuditTarget target,
        Guid batchId,
        string requestedUrl,
        Guid requestedByUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await FindActiveRunAsync(target.Id, cancellationToken) is not null)
        {
            return null;
        }

        var runId = OpenRun(
            target, batchId, requestedUrl, PageAuditSources.Manual, requestedByUserId, now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (await FindActiveRunAsync(target.Id, cancellationToken) is null)
            {
                throw;
            }

            return null;
        }

        dbContext.ChangeTracker.Clear();
        return runId;
    }

    private async Task<IReadOnlyList<Guid>> OpenDueRunsAsync(CancellationToken cancellationToken)
    {
        if (!providerOptions.HasApiKey)
        {
            return [];
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var targetIds = await ClaimDueTargetIdsAsync(now, options.DispatchBatchSize, cancellationToken);
        if (targetIds.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        var targets = await dbContext.PageAuditTargets
            .Where(target => targetIds.Contains(target.Id))
            .OrderBy(target => target.NextDueAt)
            .ThenBy(target => target.Id)
            .ToArrayAsync(cancellationToken);

        var endpointIds = targets.Select(target => target.EndpointId).Distinct().ToArray();
        var eligible = await MonitoringEligibility
            .ApplyTestable(dbContext.Endpoints.AsNoTracking())
            .Where(endpoint => endpointIds.Contains(endpoint.Id))
            .Select(endpoint => new { endpoint.Id, endpoint.NormalizedUrl })
            .ToDictionaryAsync(endpoint => endpoint.Id, endpoint => endpoint.NormalizedUrl, cancellationToken);

        var activeTargetIds = (await dbContext.PageAuditRuns.AsNoTracking()
            .Where(run => targetIds.Contains(run.PageAuditTargetId)
                && (run.Status == PageAuditRunStatuses.Queued
                    || run.Status == PageAuditRunStatuses.Running))
            .Select(run => run.PageAuditTargetId)
            .ToArrayAsync(cancellationToken)).ToHashSet();

        var opened = new List<Guid>();
        foreach (var targetGroup in targets.GroupBy(target => new { target.EndpointId, target.Strategy }))
        {
            var batchId = Guid.NewGuid();
            Guid? representative = null;
            foreach (var target in targetGroup)
            {
                target.NextDueAt = MonitorCadence.GetFirstSlotAfter(
                    target.ScheduleAnchor, target.IntervalSeconds, now);
                target.UpdatedAt = now;

                if (activeTargetIds.Contains(target.Id))
                {
                    continue;
                }

                if (!eligible.TryGetValue(target.EndpointId, out var normalizedUrl)
                    || !PageAuditEligibility.Evaluate(normalizedUrl).IsEligible)
                {
                    continue;
                }

                var openedRunId = OpenRun(
                    target, batchId, normalizedUrl, PageAuditSources.Scheduled, null, now);
                representative ??= openedRunId;
            }

            if (representative is { } runId)
            {
                opened.Add(runId);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return opened;
    }

    private Guid OpenRun(
        PageAuditTarget target,
        Guid batchId,
        string requestedUrl,
        string source,
        Guid? requestedByUserId,
        DateTimeOffset now)
    {
        var runId = Guid.NewGuid();
        dbContext.PageAuditRuns.Add(new PageAuditRun
        {
            Id = runId,
            BatchId = batchId,
            PageAuditTargetId = target.Id,
            EndpointId = target.EndpointId,
            Source = source,
            InitiatedByUserId = requestedByUserId,
            Status = PageAuditRunStatuses.Queued,

            RequestedUrl = requestedUrl,
            Provider = target.Provider,
            Category = target.Category,
            Strategy = target.Strategy,
            Locale = providerOptions.Locale,
            AttemptCount = 0,
            QueuedAt = now,
            UpdatedAt = now
        });
        return runId;
    }

    private async Task RetireUnreachableRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await dbContext.PageAuditRuns
            .Where(run => run.Id == runId && run.Status == PageAuditRunStatuses.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, PageAuditRunStatuses.Cancelled)
                .SetProperty(run => run.SafeDiagnostic,
                    "No page audit worker is running on this instance, so the audit was never started.")
                .SetProperty(run => run.FinishedAt, now)
                .SetProperty(run => run.UpdatedAt, now),
                cancellationToken);
        dbContext.ChangeTracker.Clear();
    }

    private async Task<Guid?> FindActiveRunAsync(Guid targetId, CancellationToken cancellationToken)
    {
        var runId = await dbContext.PageAuditRuns.AsNoTracking()
            .Where(run => run.PageAuditTargetId == targetId
                && (run.Status == PageAuditRunStatuses.Queued
                    || run.Status == PageAuditRunStatuses.Running))
            .Select(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return runId == Guid.Empty ? null : runId;
    }

    private int EnqueueAll(IReadOnlyList<Guid> runIds)
    {
        foreach (var runId in runIds)
        {
            queue.Enqueue(runId);
        }

        return runIds.Count;
    }

    private async Task<IReadOnlyList<Guid>> ClaimDueTargetIdsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(
            """
            WITH due_batch AS (
                SELECT candidate.endpoint_id, candidate.strategy, min(candidate.next_due_at) AS next_due_at
                FROM web_health.page_audit_target AS candidate
                WHERE candidate.is_enabled
                  AND candidate.scheduling_enabled
                  AND candidate.next_due_at <= @now
                GROUP BY candidate.endpoint_id, candidate.strategy
                ORDER BY min(candidate.next_due_at), candidate.endpoint_id, candidate.strategy
                LIMIT @limit
            )
            SELECT target.id
            FROM web_health.page_audit_target AS target
            JOIN due_batch
              ON due_batch.endpoint_id = target.endpoint_id
             AND due_batch.strategy = target.strategy
            WHERE target.is_enabled
              AND target.scheduling_enabled
              AND target.next_due_at <= @now
            ORDER BY target.next_due_at, target.id
            FOR UPDATE OF target SKIP LOCKED
            """,
            connection,
            (NpgsqlTransaction)dbContext.Database.CurrentTransaction!.GetDbTransaction());
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private static string DescribeIneligibility(string? reason) => reason switch
    {
        PageAuditIneligibilityReasons.HostNotPublic or PageAuditIneligibilityReasons.AddressNotPublic =>
            "This URL is not reachable from the public internet, so Google cannot audit it.",
        PageAuditIneligibilityReasons.UrlCarriesCredentials =>
            "This URL carries credentials, which must not be sent to a third party.",
        PageAuditIneligibilityReasons.SchemeNotSupported =>
            "Only http and https pages can be audited.",
        _ => "This endpoint URL cannot be audited."
    };
}
