using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PageAudits;

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

    private async Task<PageAuditExecutionOutcome> HandleProviderFailureAsync(
        IReadOnlyList<PageAuditRun> claims,
        PageAuditProviderException exception,
        CancellationToken cancellationToken)
    {
        var primary = claims[0];
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
