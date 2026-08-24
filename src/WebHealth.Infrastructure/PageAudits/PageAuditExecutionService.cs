using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PageAudits;

/// <summary>
/// Runs one audit: claims it, calls the provider, and records what came back.
/// </summary>
/// <remarks>
/// <para>
/// The provider call happens between two short transactions and inside neither. A transaction held
/// open across a ninety-second call to Google would hold its locks for ninety seconds, and the row
/// it holds is the one the reconciliation sweep needs to read.
/// </para>
/// <para>
/// Idempotency comes from the lease rather than from the job being delivered once. Hangfire
/// promises at-least-once delivery, so the second delivery is a case to handle, not a bug to
/// prevent: it finds a terminal run or a valid lease and returns having done nothing.
/// </para>
/// </remarks>
public sealed class PageAuditExecutionService(
    ApplicationDbContext dbContext,
    IPageAuditProvider provider,
    IPageAuditIncidentAutomationService incidentAutomation,
    PageAuditSchedulingOptions options,
    TimeProvider timeProvider,
    ILogger<PageAuditExecutionService> logger)
{
    public async Task<PageAuditExecutionOutcome> ExecuteAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var claim = await ClaimAsync(runId, cancellationToken);
        if (claim is null)
        {
            logger.LogInformation(
                "PageAudit run not claimed. PageAuditRunId={PageAuditRunId}", runId);
            return PageAuditExecutionOutcome.NotClaimed(runId);
        }

        // Re-checked after the claim, not before: enabling and authorization can change between
        // the dispatcher queueing the run and a worker picking it up, and the request that
        // actually leaves this process must be the one the current configuration permits.
        var ineligible = await FindIneligibilityAsync(claim, cancellationToken);
        if (ineligible is not null)
        {
            return await FailAsync(
                claim, PageAuditFailureCategories.TargetRejected, ineligible, cancellationToken);
        }

        try
        {
            var result = await provider.RunAsync(
                new PageAuditRequest(
                    new Uri(claim[0].RequestedUrl),
                    claim.Select(run => run.Category).ToArray(),
                    claim[0].Strategy,
                    claim[0].Locale),
                cancellationToken);
            return await CompleteAsync(claim, result, cancellationToken);
        }
        catch (PageAuditProviderException exception)
        {
            return await HandleProviderFailureAsync(claim, exception, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An adapter fault the provider did not name - a response shape its guards did not
            // anticipate, say. Left to escape it would reach the job, leave the run Running with a
            // lease, and be reclaimed by reconciliation for as long as the fault reproduces: an
            // unbounded retry loop against somebody else's quota. Only the exception type is
            // recorded, never its message, which is unbounded text from an unknown source.
            logger.LogError(
                exception,
                "PageAudit run faulted unexpectedly. PageAuditRunId={PageAuditRunId} "
                + "EndpointId={EndpointId}",
                claim[0].Id, claim[0].EndpointId);
            return await FailAsync(
                claim,
                PageAuditFailureCategories.UnknownProviderFailure,
                $"The audit failed unexpectedly ({exception.GetType().Name}).",
                CancellationToken.None);
        }
    }

    /// <summary>
    /// Takes the run only when it is Queued, or Running with a claim that has expired. The update
    /// is conditional in the database rather than checked in memory, so two workers racing here
    /// produce one winner and one no-op rather than two audits.
    /// </summary>
    private async Task<IReadOnlyList<PageAuditRun>?> ClaimAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseToken = Guid.NewGuid();

        var seed = await dbContext.PageAuditRuns.AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => new { run.BatchId, run.Strategy })
            .SingleOrDefaultAsync(cancellationToken);
        if (seed is null)
        {
            return null;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // The attempt ceiling is part of the claim, not only of the retry decision. Without it a
        // run whose worker dies - or whose fault escapes before any decision is recorded - is
        // reclaimed by reconciliation forever, and every reclaim is another request against
        // somebody else's site.
        var claimed = await dbContext.PageAuditRuns
            .Where(run => (seed.BatchId == Guid.Empty
                    ? run.Id == runId
                    : run.BatchId == seed.BatchId && run.Strategy == seed.Strategy)
                && run.AttemptCount < options.MaximumAttempts
                && (run.Status == PageAuditRunStatuses.Queued
                    || (run.Status == PageAuditRunStatuses.Running
                        && run.LeaseExpiresAt != null
                        && run.LeaseExpiresAt < now)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, PageAuditRunStatuses.Running)
                .SetProperty(run => run.LeaseToken, leaseToken)
                .SetProperty(run => run.LeaseExpiresAt, now.Add(options.LeaseDuration))
                .SetProperty(run => run.AttemptCount, run => run.AttemptCount + 1)
                .SetProperty(run => run.UpdatedAt, now),
                cancellationToken);

        if (claimed == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var runs = await dbContext.PageAuditRuns.AsNoTracking()
            .Where(candidate => candidate.LeaseToken == leaseToken)
            .OrderBy(candidate => candidate.Category)
            .ThenBy(candidate => candidate.Id)
            .ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return runs;
    }

    /// <summary>
    /// Why this run must not be sent, or null. Covers the whole chain the dispatcher checked,
    /// because a worker can pick the run up long after the dispatcher queued it.
    /// </summary>
    private async Task<string?> FindIneligibilityAsync(
        IReadOnlyList<PageAuditRun> runs,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var first = runs[0];
        var targetIds = runs.Select(run => run.PageAuditTargetId).ToArray();

        var enabledTargetCount = await dbContext.PageAuditTargets.AsNoTracking()
            .CountAsync(target => targetIds.Contains(target.Id) && target.IsEnabled, cancellationToken);
        if (enabledTargetCount != targetIds.Length)
        {
            return "PageSpeed auditing was switched off for this endpoint before the run started.";
        }

        var current = await MonitoringEligibility
            .ApplyTestable(dbContext.Endpoints.AsNoTracking(), now)
            .Where(endpoint => endpoint.Id == first.EndpointId)
            .Select(endpoint => endpoint.NormalizedUrl)
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return "The endpoint is no longer active, or its target authorization has lapsed.";
        }

        // The snapshot is what makes the job un-steerable, but it also makes it stale. An endpoint
        // edited from A to B between queueing and execution re-derives its authorization for B,
        // so the check above passes while the request still carries A - a host nobody authorized.
        // The snapshot is only trustworthy while it still is the endpoint's URL.
        if (!runs.All(run => string.Equals(current, run.RequestedUrl, StringComparison.Ordinal)))
        {
            return "The endpoint URL changed after this run was queued, so the audit it was "
                + "opened for no longer describes this endpoint.";
        }

        var eligibility = PageAuditEligibility.Evaluate(first.RequestedUrl);
        return eligibility.IsEligible
            ? null
            : $"The endpoint URL is not eligible for a public audit: {eligibility.Reason}.";
    }

    private async Task<PageAuditExecutionOutcome> CompleteAsync(
        IReadOnlyList<PageAuditRun> claims,
        PageAuditProviderBatchResult batchResult,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var primary = claims[0];
        if (claims.Any(claim => !batchResult.Categories.ContainsKey(claim.Category)))
        {
            return await FailAsync(
                claims,
                PageAuditFailureCategories.ProviderContractInvalid,
                "The provider response did not contain every requested category.",
                cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var claim in claims)
        {
            var result = batchResult.Categories[claim.Category];
            var warningSummary = PageAuditNormalization.SummarizeWarnings(
                result.Warnings, PageAuditTextBounds.WarningSummary);
            var status = warningSummary is null
                ? PageAuditRunStatuses.Completed
                : PageAuditRunStatuses.CompletedWithWarnings;
            var stillOurs = await dbContext.PageAuditRuns
                .Where(run => run.Id == claim.Id && run.LeaseToken == claim.LeaseToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, status)
                    .SetProperty(run => run.RawScore, result.CategoryScore)
                    .SetProperty(run => run.FinalUrl,
                        PageAuditNormalization.BoundText(result.FinalUrl, PageAuditTextBounds.Url))
                    .SetProperty(run => run.LighthouseVersion,
                        PageAuditNormalization.BoundText(
                            result.LighthouseVersion, PageAuditTextBounds.LighthouseVersion))
                    .SetProperty(run => run.AnalysisAt, result.AnalysisAt)
                    .SetProperty(run => run.WarningSummary, warningSummary)
                    .SetProperty(run => run.FailureCategory, (string?)null)
                    .SetProperty(run => run.SafeDiagnostic, (string?)null)
                    .SetProperty(run => run.FinishedAt, now)
                    .SetProperty(run => run.LeaseToken, (Guid?)null)
                    .SetProperty(run => run.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(run => run.UpdatedAt, now),
                    cancellationToken);

            if (stillOurs == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                logger.LogWarning(
                    "PageAudit result discarded: the lease had moved on. PageAuditRunId={PageAuditRunId}",
                    claim.Id);
                return PageAuditExecutionOutcome.NotClaimed(primary.Id);
            }
        }

        var runIds = claims.Select(claim => claim.Id).ToArray();
        await dbContext.PageAuditItems
            .Where(item => runIds.Contains(item.RunId))
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var claim in claims)
        {
            var result = batchResult.Categories[claim.Category];
            dbContext.PageAuditItems.AddRange(result.Items.Select(item => ToEntity(claim.Id, item)));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var claim in claims)
        {
            await incidentAutomation.ApplyAsync(
                new(claim.Id, claim.EndpointId, claim.Source, claim.Category, claim.Strategy),
                batchResult.Categories[claim.Category],
                now,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        var failedCount = batchResult.Categories.Values.Sum(result => result.Items.Count(item =>
            PageAuditNormalization.CountsAsFailure(PageAuditNormalization.ClassifyAuditStatus(
                item.ScoreDisplayMode, item.Score, item.ErrorMessage))));
        var itemCount = batchResult.Categories.Values.Sum(result => result.Items.Count);
        logger.LogInformation(
            "PageAudit batch completed. PageAuditBatchId={PageAuditBatchId} EndpointId={EndpointId} "
            + "AuditItemCount={AuditItemCount} FailedAuditCount={FailedAuditCount} "
            + "LighthouseVersion={LighthouseVersion} AttemptNumber={AttemptNumber}",
            primary.BatchId, primary.EndpointId, itemCount, failedCount,
            batchResult.Categories.Values.First().LighthouseVersion, primary.AttemptCount);

        var outcomeStatus = batchResult.Categories.Values.Any(result => result.Warnings.Count > 0)
            ? PageAuditRunStatuses.CompletedWithWarnings
            : PageAuditRunStatuses.Completed;
        return new PageAuditExecutionOutcome(primary.Id, outcomeStatus, null, null);
    }

    private PageAuditItem ToEntity(Guid runId, PageAuditProviderItem item) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        AuditId = PageAuditNormalization.BoundText(item.AuditId, PageAuditTextBounds.AuditId)!,
        Status = PageAuditNormalization.ClassifyAuditStatus(
            item.ScoreDisplayMode, item.Score, item.ErrorMessage),
        Score = PageAuditNormalization.NormalizeCategoryScore(item.Score),
        ScoreDisplayMode = PageAuditNormalization.BoundText(
            item.ScoreDisplayMode, PageAuditTextBounds.ScoreDisplayMode),
        NumericValue = item.NumericValue,
        NumericUnit = PageAuditNormalization.BoundText(item.NumericUnit, PageAuditTextBounds.NumericUnit),
        Weight = double.IsFinite(item.Weight) && item.Weight >= 0 ? item.Weight : 0,
        GroupName = PageAuditNormalization.BoundText(item.Group, PageAuditTextBounds.GroupName),
        Title = PageAuditNormalization.BoundText(item.Title, PageAuditTextBounds.Title),
        Description = PageAuditNormalization.BoundText(item.Description, PageAuditTextBounds.Description),
        DisplayValue = PageAuditNormalization.BoundText(item.DisplayValue, PageAuditTextBounds.DisplayValue),
        Explanation = PageAuditNormalization.BoundText(item.Explanation, PageAuditTextBounds.Explanation),
        ErrorMessage = PageAuditNormalization.BoundText(item.ErrorMessage, PageAuditTextBounds.ErrorMessage)
    };

    /// <summary>
    /// A transient failure with attempts left leaves the run alive and queued again. Anything else
    /// ends it. The attempt count is the application's, not Hangfire's, so the two cannot disagree
    /// about how many times we have already asked Google for this page.
    /// </summary>
    private async Task<PageAuditExecutionOutcome> HandleProviderFailureAsync(
        IReadOnlyList<PageAuditRun> claims,
        PageAuditProviderException exception,
        CancellationToken cancellationToken)
    {
        var primary = claims[0];
        // Cancellation is not a failure of the audit, and it is the one case where the token this
        // method was handed is already cancelled. Writing the terminal row with it would cancel
        // the write too, leaving the run Running until its lease expired.
        if (exception.FailureCategory == PageAuditFailureCategories.Cancelled)
        {
            return await FinishAsync(
                claims,
                PageAuditRunStatuses.Cancelled,
                PageAuditFailureCategories.Cancelled,
                exception.Message,
                CancellationToken.None);
        }

        var retryable = PageAuditFailureCategories.IsTransient(exception.FailureCategory)
            && primary.AttemptCount < options.MaximumAttempts;
        if (!retryable)
        {
            return await FailAsync(claims, exception.FailureCategory, exception.Message, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var delay = exception.RetryAfter ?? BackoffFor(primary.AttemptCount);

        var claimIds = claims.Select(claim => claim.Id).ToArray();
        var stillOurs = await dbContext.PageAuditRuns
            .Where(run => claimIds.Contains(run.Id) && run.LeaseToken == primary.LeaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, PageAuditRunStatuses.Queued)
                .SetProperty(run => run.FailureCategory, exception.FailureCategory)
                .SetProperty(run => run.SafeDiagnostic,
                    PageAuditNormalization.BoundText(exception.Message, PageAuditTextBounds.SafeDiagnostic))
                .SetProperty(run => run.LeaseToken, (Guid?)null)
                .SetProperty(run => run.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(run => run.UpdatedAt, now),
                cancellationToken);
        if (stillOurs != claims.Count)
        {
            return PageAuditExecutionOutcome.NotClaimed(primary.Id);
        }

        logger.LogWarning(
            "PageAudit attempt failed and will be retried. PageAuditRunId={PageAuditRunId} "
            + "EndpointId={EndpointId} FailureCategory={FailureCategory} AttemptNumber={AttemptNumber}",
            primary.Id, primary.EndpointId, exception.FailureCategory, primary.AttemptCount);

        return new PageAuditExecutionOutcome(
            primary.Id, PageAuditRunStatuses.Queued, exception.FailureCategory, delay);
    }

    /// <summary>
    /// Immediate, then a minute, then five. Spread far enough apart that a provider having a bad
    /// minute gets one, and close enough together that a daily audit still lands the same day.
    /// </summary>
    private static TimeSpan BackoffFor(int attemptCount) => attemptCount switch
    {
        <= 1 => TimeSpan.FromSeconds(60),
        _ => TimeSpan.FromMinutes(5)
    };

    private Task<PageAuditExecutionOutcome> FailAsync(
        IReadOnlyList<PageAuditRun> claims,
        string failureCategory,
        string diagnostic,
        CancellationToken cancellationToken) =>
        FinishAsync(claims, PageAuditRunStatuses.Failed, failureCategory, diagnostic, cancellationToken);

    private async Task<PageAuditExecutionOutcome> FinishAsync(
        IReadOnlyList<PageAuditRun> claims,
        string status,
        string failureCategory,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var primary = claims[0];
        var claimIds = claims.Select(claim => claim.Id).ToArray();
        var stillOurs = await dbContext.PageAuditRuns
            .Where(run => claimIds.Contains(run.Id) && run.LeaseToken == primary.LeaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, status)
                .SetProperty(run => run.FailureCategory, failureCategory)
                .SetProperty(run => run.SafeDiagnostic,
                    PageAuditNormalization.BoundText(diagnostic, PageAuditTextBounds.SafeDiagnostic))
                .SetProperty(run => run.FinishedAt, now)
                .SetProperty(run => run.LeaseToken, (Guid?)null)
                .SetProperty(run => run.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(run => run.UpdatedAt, now),
                cancellationToken);
        if (stillOurs != claims.Count)
        {
            return PageAuditExecutionOutcome.NotClaimed(primary.Id);
        }

        logger.LogWarning(
            "PageAudit run failed. PageAuditRunId={PageAuditRunId} EndpointId={EndpointId} "
            + "FailureCategory={FailureCategory} AttemptNumber={AttemptNumber}",
            primary.Id, primary.EndpointId, failureCategory, primary.AttemptCount);

        return new PageAuditExecutionOutcome(
            primary.Id, PageAuditRunStatuses.Failed, failureCategory, null);
    }
}
