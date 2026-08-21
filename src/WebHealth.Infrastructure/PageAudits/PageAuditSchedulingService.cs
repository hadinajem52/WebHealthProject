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

/// <summary>
/// Decides which audits to ask for and opens the runs that record them.
/// </summary>
/// <remarks>
/// The run row is committed before the job is enqueued, never the other way round. A job that
/// arrived before its row existed would find nothing to do and disappear; a row with no job is
/// visible, and <see cref="ReconcileAsync" /> can find it. The failure that survives is the one
/// that leaves evidence.
/// </remarks>
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

    /// <summary>
    /// Picks up runs whose job never arrived or whose worker died. It re-enqueues the same run id
    /// rather than opening a second run: the execution service claims by lease, so a duplicate
    /// delivery is already harmless, and a duplicate <em>run</em> would be a second API call.
    /// </summary>
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
            .Select(run => new { run.Id, run.AttemptCount })
            .ToArrayAsync(cancellationToken);

        // A run that has spent its attempts can never be claimed again, so re-enqueueing it would
        // queue work no worker will do while its target's active-run index refuses every new run.
        // Retiring it is what lets the next scheduled audit open at all.
        var exhausted = recoverable
            .Where(run => run.AttemptCount >= options.MaximumAttempts)
            .Select(run => run.Id)
            .ToArray();
        if (exhausted.Length > 0)
        {
            await dbContext.PageAuditRuns
                .Where(run => exhausted.Contains(run.Id))
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
                "Retired {Count} page audit runs that had no attempts left.", exhausted.Length);
        }

        var runIds = recoverable
            .Where(run => run.AttemptCount < options.MaximumAttempts)
            .Select(run => run.Id)
            .ToArray();

        if (runIds.Length > 0)
        {
            logger.LogInformation(
                "Re-enqueueing {Count} page audit runs left behind by a lost job or a stopped worker.",
                runIds.Length);
        }

        return EnqueueAll(runIds);
    }

    /// <summary>
    /// Opens a run somebody asked for by hand. Returns the run already in flight rather than
    /// failing when one exists: the person wants a fresh score, and a run that is about to produce
    /// one satisfies that better than an error does.
    /// </summary>
    public async Task<PageAuditManualResult> QueueManualAsync(
        Guid endpointId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        var now = timeProvider.GetUtcNow();

        // Enforced here rather than trusted from the caller. Asking Google to load a page is
        // active testing of that target, and this method is the only door to it.
        if (!await targetAuthorization.CanTestEndpointAsync(endpointId, access, cancellationToken))
        {
            return PageAuditManualResult.Rejected(
                "You are not authorized to run an audit against this endpoint.");
        }

        var target = await dbContext.PageAuditTargets.AsNoTracking()
            .Where(candidate => candidate.EndpointId == endpointId
                && candidate.Provider == PageAuditProviders.PageSpeedInsights
                && candidate.Category == PageAuditCategories.Seo
                && candidate.IsEnabled)
            .OrderBy(candidate => candidate.Strategy)
            .FirstOrDefaultAsync(cancellationToken);
        if (target is null)
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
            .ApplyTestable(dbContext.Endpoints.AsNoTracking(), now)
            .Where(candidate => candidate.Id == endpointId)
            .Select(candidate => new { candidate.NormalizedUrl })
            .SingleOrDefaultAsync(cancellationToken);
        if (endpoint is null)
        {
            return PageAuditManualResult.Rejected(
                "The endpoint is not active, or its target authorization has lapsed.");
        }

        var eligibility = PageAuditEligibility.Evaluate(endpoint.NormalizedUrl);
        if (!eligibility.IsEligible)
        {
            return PageAuditManualResult.Rejected(DescribeIneligibility(eligibility.Reason));
        }

        var existing = await dbContext.PageAuditRuns.AsNoTracking()
            .Where(run => run.PageAuditTargetId == target.Id
                && (run.Status == PageAuditRunStatuses.Queued
                    || run.Status == PageAuditRunStatuses.Running))
            .Select(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing != Guid.Empty)
        {
            return PageAuditManualResult.AlreadyRunning(existing);
        }

        var runId = OpenRun(target, endpoint.NormalizedUrl, PageAuditSources.Manual, access.UserId, now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The partial unique index caught a request that raced the dispatcher. The other run
            // is doing exactly what this one would have, so it is the answer rather than an error.
            // Anything else is a real failure and is rethrown.
            dbContext.ChangeTracker.Clear();
            var winner = await FindActiveRunAsync(target.Id, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return PageAuditManualResult.AlreadyRunning(winner.Value);
        }

        dbContext.ChangeTracker.Clear();
        try
        {
            queue.Enqueue(runId);
        }
        catch (Exception exception)
        {
            // The run is committed before it is enqueued so a lost job leaves evidence rather than
            // nothing. That bargain assumes a worker exists to lose the job. With scheduling off
            // there is no queue and no reconciliation sweep, so a committed run would sit Queued
            // forever and its target's active-run index would refuse every later request. Retiring
            // it here keeps the failure to the one request that caused it.
            await RetireUnreachableRunAsync(runId, cancellationToken);
            logger.LogError(
                exception,
                "PageAudit run could not be queued and was retired. PageAuditRunId={PageAuditRunId}",
                runId);
            return PageAuditManualResult.Rejected(
                "PageSpeed audits are not running on this instance, so the audit could not be "
                + "started. Enable PageAudits:Scheduling to run them.");
        }

        logger.LogInformation(
            "PageAudit run queued by request. PageAuditRunId={PageAuditRunId} EndpointId={EndpointId}",
            runId, endpointId);
        return PageAuditManualResult.Queued(runId);
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
            .ApplyTestable(dbContext.Endpoints.AsNoTracking(), now)
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
        foreach (var target in targets)
        {
            // The cadence advances whether or not a run is opened. A target that is ineligible
            // today must not accumulate a backlog of missed slots to fire the moment it is fixed.
            target.NextDueAt = MonitorCadence.GetFirstSlotAfter(
                target.ScheduleAnchor, target.IntervalSeconds, now);
            target.UpdatedAt = now;

            if (activeTargetIds.Contains(target.Id))
            {
                // The previous run has not finished. Skipping the slot is the honest behaviour:
                // the audit is already in flight, and a second one would spend quota to overtake it.
                continue;
            }

            if (!eligible.TryGetValue(target.EndpointId, out var normalizedUrl)
                || !PageAuditEligibility.Evaluate(normalizedUrl).IsEligible)
            {
                continue;
            }

            opened.Add(OpenRun(target, normalizedUrl, PageAuditSources.Scheduled, null, now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return opened;
    }

    private Guid OpenRun(
        PageAuditTarget target,
        string requestedUrl,
        string source,
        Guid? requestedByUserId,
        DateTimeOffset now)
    {
        var runId = Guid.NewGuid();
        dbContext.PageAuditRuns.Add(new PageAuditRun
        {
            Id = runId,
            PageAuditTargetId = target.Id,
            EndpointId = target.EndpointId,
            Source = source,
            InitiatedByUserId = requestedByUserId,
            Status = PageAuditRunStatuses.Queued,

            // Snapshotted here, so the job receives a run id and nothing else. A job that took a
            // URL would be a job that could be handed a different one.
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

    /// <summary>
    /// Closes a run that was committed but could never be handed to a worker, so it does not hold
    /// its target's single active-run slot against every later request.
    /// </summary>
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

    /// <summary>
    /// <c>SKIP LOCKED</c> so two dispatchers running at once divide the work rather than block on
    /// each other, and neither hands the same target to two workers.
    /// </summary>
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
            SELECT target.id
            FROM web_health.page_audit_target AS target
            WHERE target.is_enabled
              AND target.scheduling_enabled
              AND target.next_due_at <= @now
            ORDER BY target.next_due_at, target.id
            FOR UPDATE OF target SKIP LOCKED
            LIMIT @limit
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
