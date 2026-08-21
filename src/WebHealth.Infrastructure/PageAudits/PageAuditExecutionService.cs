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
                    new Uri(claim.RequestedUrl),
                    claim.Category,
                    claim.Strategy,
                    claim.Locale),
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
                claim.Id, claim.EndpointId);
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
    private async Task<PageAuditRun?> ClaimAsync(Guid runId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseToken = Guid.NewGuid();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // The attempt ceiling is part of the claim, not only of the retry decision. Without it a
        // run whose worker dies - or whose fault escapes before any decision is recorded - is
        // reclaimed by reconciliation forever, and every reclaim is another request against
        // somebody else's site.
        var claimed = await dbContext.PageAuditRuns
            .Where(run => run.Id == runId
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

        var run = await dbContext.PageAuditRuns.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == runId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return run;
    }

    /// <summary>
    /// Why this run must not be sent, or null. Covers the whole chain the dispatcher checked,
    /// because a worker can pick the run up long after the dispatcher queued it.
    /// </summary>
    private async Task<string?> FindIneligibilityAsync(
        PageAuditRun run,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var targetEnabled = await dbContext.PageAuditTargets.AsNoTracking()
            .AnyAsync(target => target.Id == run.PageAuditTargetId && target.IsEnabled, cancellationToken);
        if (!targetEnabled)
        {
            return "PageSpeed auditing was switched off for this endpoint before the run started.";
        }

        var current = await MonitoringEligibility
            .ApplyTestable(dbContext.Endpoints.AsNoTracking(), now)
            .Where(endpoint => endpoint.Id == run.EndpointId)
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
        if (!string.Equals(current, run.RequestedUrl, StringComparison.Ordinal))
        {
            return "The endpoint URL changed after this run was queued, so the audit it was "
                + "opened for no longer describes this endpoint.";
        }

        var eligibility = PageAuditEligibility.Evaluate(run.RequestedUrl);
        return eligibility.IsEligible
            ? null
            : $"The endpoint URL is not eligible for a public audit: {eligibility.Reason}.";
    }

    private async Task<PageAuditExecutionOutcome> CompleteAsync(
        PageAuditRun claim,
        PageAuditProviderResult result,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var warningSummary = PageAuditNormalization.SummarizeWarnings(
            result.Warnings, PageAuditTextBounds.WarningSummary);
        var status = warningSummary is null
            ? PageAuditRunStatuses.Completed
            : PageAuditRunStatuses.CompletedWithWarnings;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The lease is verified as part of the update rather than read first. Between a read and a
        // write the lease could expire and another worker could claim the run, and this worker
        // would then overwrite that worker's result with its own.
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
            return PageAuditExecutionOutcome.NotClaimed(claim.Id);
        }

        // A reclaimed run may already carry items from the attempt that lost its lease. Clearing
        // first keeps the run's items describing exactly one provider response.
        await dbContext.PageAuditItems
            .Where(item => item.RunId == claim.Id)
            .ExecuteDeleteAsync(cancellationToken);
        dbContext.PageAuditItems.AddRange(result.Items.Select(item => ToEntity(claim.Id, item)));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        var failedCount = result.Items.Count(item => PageAuditNormalization.CountsAsFailure(
            PageAuditNormalization.ClassifyAuditStatus(
                item.ScoreDisplayMode, item.Score, item.ErrorMessage)));
        logger.LogInformation(
            "PageAudit run completed. PageAuditRunId={PageAuditRunId} EndpointId={EndpointId} "
            + "RunStatus={RunStatus} AuditItemCount={AuditItemCount} FailedAuditCount={FailedAuditCount} "
            + "LighthouseVersion={LighthouseVersion} AttemptNumber={AttemptNumber}",
            claim.Id, claim.EndpointId, status, result.Items.Count, failedCount,
            result.LighthouseVersion, claim.AttemptCount);

        return new PageAuditExecutionOutcome(claim.Id, status, null, null);
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
        PageAuditRun claim,
        PageAuditProviderException exception,
        CancellationToken cancellationToken)
    {
        // Cancellation is not a failure of the audit, and it is the one case where the token this
        // method was handed is already cancelled. Writing the terminal row with it would cancel
        // the write too, leaving the run Running until its lease expired.
        if (exception.FailureCategory == PageAuditFailureCategories.Cancelled)
        {
            return await FinishAsync(
                claim,
                PageAuditRunStatuses.Cancelled,
                PageAuditFailureCategories.Cancelled,
                exception.Message,
                CancellationToken.None);
        }

        var retryable = PageAuditFailureCategories.IsTransient(exception.FailureCategory)
            && claim.AttemptCount < options.MaximumAttempts;
        if (!retryable)
        {
            return await FailAsync(claim, exception.FailureCategory, exception.Message, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var delay = exception.RetryAfter ?? BackoffFor(claim.AttemptCount);

        var stillOurs = await dbContext.PageAuditRuns
            .Where(run => run.Id == claim.Id && run.LeaseToken == claim.LeaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, PageAuditRunStatuses.Queued)
                .SetProperty(run => run.FailureCategory, exception.FailureCategory)
                .SetProperty(run => run.SafeDiagnostic,
                    PageAuditNormalization.BoundText(exception.Message, PageAuditTextBounds.SafeDiagnostic))
                .SetProperty(run => run.LeaseToken, (Guid?)null)
                .SetProperty(run => run.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(run => run.UpdatedAt, now),
                cancellationToken);
        if (stillOurs == 0)
        {
            return PageAuditExecutionOutcome.NotClaimed(claim.Id);
        }

        logger.LogWarning(
            "PageAudit attempt failed and will be retried. PageAuditRunId={PageAuditRunId} "
            + "EndpointId={EndpointId} FailureCategory={FailureCategory} AttemptNumber={AttemptNumber}",
            claim.Id, claim.EndpointId, exception.FailureCategory, claim.AttemptCount);

        return new PageAuditExecutionOutcome(
            claim.Id, PageAuditRunStatuses.Queued, exception.FailureCategory, delay);
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
        PageAuditRun claim,
        string failureCategory,
        string diagnostic,
        CancellationToken cancellationToken) =>
        FinishAsync(claim, PageAuditRunStatuses.Failed, failureCategory, diagnostic, cancellationToken);

    private async Task<PageAuditExecutionOutcome> FinishAsync(
        PageAuditRun claim,
        string status,
        string failureCategory,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var stillOurs = await dbContext.PageAuditRuns
            .Where(run => run.Id == claim.Id && run.LeaseToken == claim.LeaseToken)
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
        if (stillOurs == 0)
        {
            return PageAuditExecutionOutcome.NotClaimed(claim.Id);
        }

        logger.LogWarning(
            "PageAudit run failed. PageAuditRunId={PageAuditRunId} EndpointId={EndpointId} "
            + "FailureCategory={FailureCategory} AttemptNumber={AttemptNumber}",
            claim.Id, claim.EndpointId, failureCategory, claim.AttemptCount);

        return new PageAuditExecutionOutcome(
            claim.Id, PageAuditRunStatuses.Failed, failureCategory, null);
    }
}
