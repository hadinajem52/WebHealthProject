using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.PageAudits;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;
using WebHealth.Domain.Incidents;

namespace WebHealth.IntegrationTests.Support;

/// <summary>
/// The scheduling, lease and finalization rules against a real database.
/// </summary>
/// <remarks>
/// Time is moved by updating the row rather than by freezing a clock. A frozen
/// <c>TimeProvider</c> beside fixtures created from the real one produces rows that violate
/// <c>updated_at &gt;= created_at</c>, which is a failure about the fixture rather than about the
/// rule under test.
/// </remarks>
internal static class PageAuditExecutionAssertions
{
    public static async Task VerifyAsync(
        string connectionString,
        ApplicationDbContext database,
        PageAuditSchedulingService scheduling,
        PageAuditExecutionService execution,
        IPageAuditIncidentPolicyService policyService,
        ScriptedPageAuditProvider provider,
        RecordingPageAuditQueue queue,
        Guid endpointId,
        Guid otherEndpointId,
        string endpointUrl)
    {
        var targetId = await SeedDueTargetAsync(database, endpointId);
        await VerifyEndpointPoliciesAreIsolatedAsync(
            database, policyService, endpointId, otherEndpointId);

        var runId = await VerifyDispatchOpensOneRunAsync(
            database, scheduling, queue, targetId, endpointUrl);
        await VerifyASecondDispatchDoesNotOvertakeTheRunInFlightAsync(
            database, scheduling, targetId, runId);
        await VerifyAFailingAuditStillCompletesTheRunAsync(
            database, execution, provider, runId);
        await VerifyADuplicateDeliveryCostsNoSecondApiCallAsync(execution, provider, runId);

        await VerifyAnExpiredLeaseCanBeReclaimedAsync(
            connectionString, database, execution, provider, targetId, endpointId, endpointUrl);
        await VerifyATransientFailureRetriesThenGivesUpAsync(
            database, execution, provider, targetId, endpointId, endpointUrl);
        await VerifyAProviderFailureLeavesNoInventedAuditsAsync(
            database, execution, provider, targetId, endpointId, endpointUrl);
        await VerifyReconciliationReEnqueuesRatherThanOpeningASecondRunAsync(
            connectionString, database, scheduling, queue, targetId, endpointId, endpointUrl);
        await VerifyAStaleUrlIsNeverSentAsync(
            connectionString, database, execution, provider, targetId, endpointId);
        await VerifyASpentAttemptBudgetStopsReclaimAsync(
            connectionString, database, execution, provider, scheduling, queue, targetId,
            endpointId, endpointUrl);
        await VerifyScheduledThresholdsOpenAndRecoverIncidentsAsync(
            database, execution, provider, targetId, endpointId, endpointUrl);
        await VerifyManualAuditUsesTwoStrategyBatchesAsync(
            database, scheduling, execution, provider, queue, endpointId, endpointUrl);
    }

    private static async Task VerifyManualAuditUsesTwoStrategyBatchesAsync(
        ApplicationDbContext database,
        PageAuditSchedulingService scheduling,
        PageAuditExecutionService execution,
        ScriptedPageAuditProvider provider,
        RecordingPageAuditQueue queue,
        Guid endpointId,
        string endpointUrl)
    {
        var existingProfiles = await database.PageAuditTargets.AsNoTracking()
            .Where(target => target.EndpointId == endpointId)
            .Select(target => new { target.Category, target.Strategy })
            .ToArrayAsync();
        var existing = existingProfiles
            .Select(profile => $"{profile.Category}|{profile.Strategy}")
            .ToHashSet(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        foreach (var category in PageAuditCategories.All)
        {
            foreach (var strategy in PageAuditStrategies.All)
            {
                if (existing.Contains($"{category}|{strategy}"))
                {
                    continue;
                }

                database.PageAuditTargets.Add(new PageAuditTarget
                {
                    Id = Guid.NewGuid(),
                    EndpointId = endpointId,
                    Provider = PageAuditProviders.PageSpeedInsights,
                    Category = category,
                    Strategy = strategy,
                    IsEnabled = true,
                    SchedulingEnabled = false,
                    IntervalSeconds = 86400,
                    ScheduleAnchor = now,
                    NextDueAt = now.AddDays(1),
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1
                });
            }
        }

        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var actorId = await database.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.Id == endpointId)
            .Select(endpoint => endpoint.UpdatedByUserId)
            .SingleAsync();
        queue.Enqueued.Clear();
        var callsBefore = provider.CallCount;

        var queued = await scheduling.QueueManualAsync(
            endpointId,
            new(actorId, ["Administrator"]));

        queued.Succeeded.Should().BeTrue(queued.Error);
        queued.QueuedCount.Should().Be(8);
        queue.Enqueued.Should().HaveCount(2);
        var manualRuns = await database.PageAuditRuns.AsNoTracking()
            .Where(run => run.EndpointId == endpointId
                && run.Source == PageAuditSources.Manual
                && run.Status == PageAuditRunStatuses.Queued)
            .ToArrayAsync();
        manualRuns.Should().HaveCount(8);
        manualRuns.GroupBy(run => run.BatchId).Should().HaveCount(2)
            .And.OnlyContain(batch => batch.Count() == 4);

        provider.Respond(ScriptedPageAuditProvider.MixedResult() with
        {
            RequestedUrl = endpointUrl,
            FinalUrl = endpointUrl,
            AnalysisAt = now
        });
        foreach (var runId in queue.Enqueued)
        {
            (await execution.ExecuteAsync(runId)).Status.Should().Be(PageAuditRunStatuses.Completed);
        }

        provider.CallCount.Should().Be(callsBefore + 2);
        var completed = await database.PageAuditRuns.AsNoTracking()
            .Where(run => manualRuns.Select(candidate => candidate.Id).Contains(run.Id))
            .ToArrayAsync();
        completed.Should().OnlyContain(run => run.Status == PageAuditRunStatuses.Completed);
        completed.GroupBy(run => run.BatchId).Should().OnlyContain(batch =>
            batch.Select(run => run.AnalysisAt).Distinct().Count() == 1
            && batch.Select(run => run.LighthouseVersion).Distinct().Count() == 1);
    }

    private static async Task VerifyScheduledThresholdsOpenAndRecoverIncidentsAsync(
        ApplicationDbContext database,
        PageAuditExecutionService execution,
        ScriptedPageAuditProvider provider,
        Guid targetId,
        Guid endpointId,
        string endpointUrl)
    {
        var endpoint = await database.Endpoints
            .Include(candidate => candidate.Environment)
            .SingleAsync(candidate => candidate.Id == endpointId);
        var monitorId = Guid.NewGuid();
        database.EndpointMonitors.Add(new EndpointMonitor
        {
            Id = monitorId,
            EndpointId = endpointId,
            PolicyProfileId = RegistryDefaults.PageAuditPolicyProfileId,
            MonitorType = RegistryDefaults.PageAuditMonitorType,
            BoundedOverrides = "{}",
            ScheduleAnchor = endpoint.UpdatedAt,
            NextDueAt = endpoint.UpdatedAt.AddDays(1),
            ConfigurationFingerprint = RegistryDefaults.CreatePageAuditFingerprint(
                endpoint.NormalizedUrl, endpoint.Environment.IsProduction),
            IntervalSeconds = RegistryDefaults.PageAuditIntervalSeconds,
            TimeoutSeconds = RegistryDefaults.PageAuditTimeoutSeconds,
            FailureConfirmationCount = 1,
            RecoveryConfirmationCount = 1,
            SchedulingEnabled = false,
            IsEnabled = true,
            CreatedAt = endpoint.UpdatedAt,
            CreatedByUserId = endpoint.UpdatedByUserId,
            UpdatedAt = endpoint.UpdatedAt,
            UpdatedByUserId = endpoint.UpdatedByUserId,
            Version = 1
        });
        await database.PageAuditIncidentPolicies
            .Where(policy => policy.EndpointId == endpointId)
            .ExecuteUpdateAsync(setters => setters
            .SetProperty(policy => policy.IncidentsEnabled, true)
            .SetProperty(policy => policy.SeoScoreEnabled, true)
            .SetProperty(policy => policy.SeoMinimumScore, 90)
            .SetProperty(policy => policy.Version, policy => policy.Version + 1));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var breachedRunId = await OpenQueuedRunAsync(
            database, targetId, endpointId, endpointUrl);
        provider.Respond(ScriptedPageAuditProvider.MixedResult() with { CategoryScore = 0.80m });
        (await execution.ExecuteAsync(breachedRunId)).Status.Should().Be(PageAuditRunStatuses.Completed);

        var issueKey = PageAuditIncidentIssueKeys.CategoryScore(
            PageAuditCategories.Seo, PageAuditStrategies.Mobile);
        var opened = await database.Incidents.AsNoTracking()
            .SingleAsync(incident => incident.EndpointMonitorId == monitorId
                && incident.IssueKey == issueKey);
        opened.Status.Should().Be(IncidentStatuses.Open);
        var openingEvidence = await database.IncidentEvidence.AsNoTracking()
            .SingleAsync(evidence => evidence.IncidentId == opened.Id
                && evidence.EvidenceType == IncidentEvidenceTypes.Opening);
        openingEvidence.PageAuditRunId.Should().Be(breachedRunId);
        openingEvidence.LogicalCheckId.Should().BeNull();

        var recoveredRunId = await OpenQueuedRunAsync(
            database, targetId, endpointId, endpointUrl);
        provider.Respond(ScriptedPageAuditProvider.MixedResult() with { CategoryScore = 0.95m });
        (await execution.ExecuteAsync(recoveredRunId)).Status.Should().Be(PageAuditRunStatuses.Completed);

        database.ChangeTracker.Clear();
        var recovered = await database.Incidents.AsNoTracking()
            .SingleAsync(incident => incident.Id == opened.Id);
        recovered.Status.Should().Be(IncidentStatuses.Resolved);
        (await database.IncidentEvidence.AsNoTracking()
            .CountAsync(evidence => evidence.IncidentId == opened.Id
                && evidence.PageAuditRunId == recoveredRunId))
            .Should().Be(2);
    }

    private static async Task<Guid> SeedDueTargetAsync(ApplicationDbContext database, Guid endpointId)
    {
        var now = DateTimeOffset.UtcNow;
        var targetId = Guid.NewGuid();
        database.PageAuditTargets.Add(new PageAuditTarget
        {
            Id = targetId,
            EndpointId = endpointId,
            Provider = PageAuditProviders.PageSpeedInsights,
            Category = PageAuditCategories.Seo,
            Strategy = PageAuditStrategies.Mobile,
            IsEnabled = true,
            SchedulingEnabled = true,
            IntervalSeconds = 86400,
            ScheduleAnchor = now.AddDays(-2),
            NextDueAt = now.AddMinutes(-1),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        });
        if (!await database.PageAuditIncidentPolicies
                .AnyAsync(policy => policy.EndpointId == endpointId))
        {
            database.PageAuditIncidentPolicies.Add(
                PageAuditIncidentPolicyDefaults.Create(endpointId, now));
        }

        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        return targetId;
    }

    private static async Task VerifyEndpointPoliciesAreIsolatedAsync(
        ApplicationDbContext database,
        IPageAuditIncidentPolicyService policyService,
        Guid endpointId,
        Guid otherEndpointId)
    {
        var now = DateTimeOffset.UtcNow;
        database.PageAuditIncidentPolicies.Add(
            PageAuditIncidentPolicyDefaults.Create(otherEndpointId, now));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var current = await policyService.GetAsync(endpointId);
        var command = new UpdatePageAuditIncidentPolicy(
            true,
            current.PerformanceScoreEnabled,
            73,
            current.AccessibilityScoreEnabled,
            current.AccessibilityMinimumScore,
            current.BestPracticesScoreEnabled,
            current.BestPracticesMinimumScore,
            current.SeoScoreEnabled,
            current.SeoMinimumScore,
            current.FirstContentfulPaintEnabled,
            current.FirstContentfulPaintMaximum,
            current.LargestContentfulPaintEnabled,
            current.LargestContentfulPaintMaximum,
            current.TotalBlockingTimeEnabled,
            current.TotalBlockingTimeMaximum,
            current.CumulativeLayoutShiftEnabled,
            current.CumulativeLayoutShiftMaximum,
            current.SpeedIndexEnabled,
            current.SpeedIndexMaximum,
            current.Version);
        var actorId = await database.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.Id == endpointId)
            .Select(endpoint => endpoint.UpdatedByUserId)
            .SingleAsync();

        (await policyService.UpdateAsync(endpointId, command, actorId))
            .Succeeded.Should().BeTrue();
        var updated = await policyService.GetAsync(endpointId);
        var untouched = await policyService.GetAsync(otherEndpointId);
        updated.IncidentsEnabled.Should().BeTrue();
        updated.PerformanceMinimumScore.Should().Be(73);
        untouched.IncidentsEnabled.Should().BeFalse();
        untouched.PerformanceMinimumScore.Should().Be(90);
    }

    private static async Task<Guid> VerifyDispatchOpensOneRunAsync(
        ApplicationDbContext database,
        PageAuditSchedulingService scheduling,
        RecordingPageAuditQueue queue,
        Guid targetId,
        string endpointUrl)
    {
        queue.Enqueued.Clear();
        (await scheduling.DispatchDueAsync()).Should().Be(1);

        var run = await database.PageAuditRuns.AsNoTracking()
            .SingleAsync(candidate => candidate.PageAuditTargetId == targetId);
        run.Status.Should().Be(PageAuditRunStatuses.Queued);
        run.Source.Should().Be(PageAuditSources.Scheduled);
        run.InitiatedByUserId.Should().BeNull("nobody asked for a scheduled run personally");
        run.AttemptCount.Should().Be(0);
        run.FinishedAt.Should().BeNull();

        // Snapshotted at dispatch, so the job receives a run id and nothing a caller could change.
        run.RequestedUrl.Should().Be(endpointUrl);
        run.Strategy.Should().Be(PageAuditStrategies.Mobile);
        run.Locale.Should().Be("en-US");

        queue.Enqueued.Should().ContainSingle("the run row is committed before the job is queued")
            .Which.Should().Be(run.Id);
        return run.Id;
    }

    /// <summary>
    /// The cadence advances even though no run was opened, so a target does not accumulate a
    /// backlog of missed slots to fire the moment the run in flight finishes.
    /// </summary>
    private static async Task VerifyASecondDispatchDoesNotOvertakeTheRunInFlightAsync(
        ApplicationDbContext database,
        PageAuditSchedulingService scheduling,
        Guid targetId,
        Guid runId)
    {
        await database.PageAuditTargets
            .Where(target => target.Id == targetId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(target => target.NextDueAt, DateTimeOffset.UtcNow.AddMinutes(-1)));

        (await scheduling.DispatchDueAsync()).Should().Be(0);

        (await database.PageAuditRuns.AsNoTracking()
            .CountAsync(run => run.PageAuditTargetId == targetId))
            .Should().Be(1, "a second run would spend quota to overtake the one already queued");
        (await database.PageAuditTargets.AsNoTracking()
            .Where(target => target.Id == targetId)
            .Select(target => target.NextDueAt)
            .SingleAsync())
            .Should().BeAfter(DateTimeOffset.UtcNow, "the slot is spent whether or not it opened a run");

        (await database.PageAuditRuns.AsNoTracking()
            .SingleAsync(run => run.Id == runId)).Status.Should().Be(PageAuditRunStatuses.Queued);
    }

    /// <summary>
    /// A page that fails every audit is a successful measurement of a bad page. Recording that as
    /// a failed run would lose the score and report a working provider as broken.
    /// </summary>
    private static async Task VerifyAFailingAuditStillCompletesTheRunAsync(
        ApplicationDbContext database,
        PageAuditExecutionService execution,
        ScriptedPageAuditProvider provider,
        Guid runId)
    {
        provider.Respond(ScriptedPageAuditProvider.MixedResult());

        var outcome = await execution.ExecuteAsync(runId);

        outcome.Status.Should().Be(PageAuditRunStatuses.Completed);
        outcome.FailureCategory.Should().BeNull();

        var run = await database.PageAuditRuns.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        run.Status.Should().Be(PageAuditRunStatuses.Completed);
        run.RawScore.Should().Be(0.8m);
        run.LighthouseVersion.Should().Be("11.4.0");
        run.FinishedAt.Should().NotBeNull();
        run.AnalysisAt.Should().NotBeNull();
        run.AttemptCount.Should().Be(1);
        run.LeaseToken.Should().BeNull("a terminal run holds no claim");
        run.LeaseExpiresAt.Should().BeNull();
        run.FailureCategory.Should().BeNull();

        var items = await database.PageAuditItems.AsNoTracking()
            .Where(item => item.RunId == runId)
            .OrderBy(item => item.AuditId)
            .ToArrayAsync();
        items.Should().HaveCount(4);
        items.Single(item => item.AuditId == "document-title").Status
            .Should().Be(PageAuditItemStatuses.Passed);
        items.Single(item => item.AuditId == "meta-description").Status
            .Should().Be(PageAuditItemStatuses.Failed);
        items.Single(item => item.AuditId == "structured-data").Status
            .Should().Be(PageAuditItemStatuses.Manual, "a manual check is not a failure");
        items.Single(item => item.AuditId == "robots-txt").Status
            .Should().Be(PageAuditItemStatuses.NotApplicable);
    }

    /// <summary>
    /// Hangfire promises at-least-once delivery, so a second delivery is a case to handle rather
    /// than a bug to prevent. It must cost nothing: no second call to Google, no duplicated items.
    /// </summary>
    private static async Task VerifyADuplicateDeliveryCostsNoSecondApiCallAsync(
        PageAuditExecutionService execution,
        ScriptedPageAuditProvider provider,
        Guid runId)
    {
        var callsBefore = provider.CallCount;

        var outcome = await execution.ExecuteAsync(runId);

        outcome.Status.Should().Be("NotClaimed");
        provider.CallCount.Should().Be(callsBefore, "a terminal run must never be audited twice");
    }

    private static async Task VerifyAnExpiredLeaseCanBeReclaimedAsync(
        string connectionString,
        ApplicationDbContext database,
        PageAuditExecutionService execution,
        ScriptedPageAuditProvider provider,
        Guid targetId,
        Guid endpointId,
        string endpointUrl)
    {
        var runId = await OpenQueuedRunAsync(database, targetId, endpointId, endpointUrl);

        // A worker that took the run and died: Running, with a claim that has since expired.
        await ExecuteSqlAsync(connectionString,
            $"""
            UPDATE web_health.page_audit_run
            SET status = 'Running', lease_token = '{Guid.NewGuid()}',
                lease_expires_at = now() - interval '10 minutes', attempt_count = 1
            WHERE id = '{runId}';
            """);

        provider.Respond(ScriptedPageAuditProvider.MixedResult());
        var outcome = await execution.ExecuteAsync(runId);

        outcome.Status.Should().Be(PageAuditRunStatuses.Completed,
            "a run abandoned by a stopped worker has to be recoverable");
        (await database.PageAuditRuns.AsNoTracking().SingleAsync(run => run.Id == runId))
            .AttemptCount.Should().Be(2, "the abandoned attempt still counts against the budget");

        await CloseRunAsync(connectionString, runId);
    }

    /// <summary>
    /// A transient failure keeps the run alive and asks for it again; the attempt budget is the
    /// application's own, so it cannot disagree with how many times we have already asked Google.
    /// </summary>
    private static async Task VerifyATransientFailureRetriesThenGivesUpAsync(
        ApplicationDbContext database,
        PageAuditExecutionService execution,
        ScriptedPageAuditProvider provider,
        Guid targetId,
        Guid endpointId,
        string endpointUrl)
    {
        var runId = await OpenQueuedRunAsync(database, targetId, endpointId, endpointUrl);
        provider.Fail(new PageAuditProviderException(
            PageAuditFailureCategories.ProviderUnavailable, "The provider is unavailable: HTTP 503."));

        var first = await execution.ExecuteAsync(runId);
        first.Status.Should().Be(PageAuditRunStatuses.Queued);
        first.ShouldRetry.Should().BeTrue();
        first.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));

        var afterFirst = await database.PageAuditRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
        afterFirst.Status.Should().Be(PageAuditRunStatuses.Queued, "the run is still alive");
        afterFirst.FinishedAt.Should().BeNull();
        afterFirst.AttemptCount.Should().Be(1);
        afterFirst.FailureCategory.Should().Be(PageAuditFailureCategories.ProviderUnavailable,
            "the run carries the reason it is being retried");
        afterFirst.LeaseToken.Should().BeNull();

        var second = await execution.ExecuteAsync(runId);
        second.ShouldRetry.Should().BeTrue();
        second.RetryAfter.Should().Be(TimeSpan.FromMinutes(5));

        // The third attempt is the last the budget allows, so it ends the run rather than asking
        // for a fourth.
        var third = await execution.ExecuteAsync(runId);
        third.Status.Should().Be(PageAuditRunStatuses.Failed);
        third.ShouldRetry.Should().BeFalse();

        var failed = await database.PageAuditRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
        failed.Status.Should().Be(PageAuditRunStatuses.Failed);
        failed.AttemptCount.Should().Be(3);
        failed.FinishedAt.Should().NotBeNull();
        failed.RawScore.Should().BeNull("a failed run has no score to report");
        failed.SafeDiagnostic.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A provider failure must not manufacture audit rows. A Lighthouse runtime error means the
    /// page was never measured, and inventing failing audits would blame the site for it.
    /// </summary>
    private static async Task VerifyAProviderFailureLeavesNoInventedAuditsAsync(
        ApplicationDbContext database,
        PageAuditExecutionService execution,
        ScriptedPageAuditProvider provider,
        Guid targetId,
        Guid endpointId,
        string endpointUrl)
    {
        var runId = await OpenQueuedRunAsync(database, targetId, endpointId, endpointUrl);
        provider.Fail(new PageAuditProviderException(
            PageAuditFailureCategories.LighthouseRuntimeError, "Lighthouse reported NO_FCP."));

        var outcome = await execution.ExecuteAsync(runId);

        outcome.Status.Should().Be(PageAuditRunStatuses.Failed);
        outcome.ShouldRetry.Should().BeFalse("a runtime error will fail identically every time");

        var run = await database.PageAuditRuns.AsNoTracking().SingleAsync(candidate => candidate.Id == runId);
        run.AttemptCount.Should().Be(1, "a non-transient failure spends one attempt, not the budget");
        run.FailureCategory.Should().Be(PageAuditFailureCategories.LighthouseRuntimeError);
        (await database.PageAuditItems.AsNoTracking().CountAsync(item => item.RunId == runId))
            .Should().Be(0);
    }

    /// <summary>
    /// The enqueue-after-commit window: a run committed whose job never arrived. Reconciliation
    /// re-enqueues the same run rather than opening a second one, because the execution service
    /// already claims by lease and a duplicate <em>run</em> would be a second API call.
    /// </summary>
    private static async Task VerifyReconciliationReEnqueuesRatherThanOpeningASecondRunAsync(
        string connectionString,
        ApplicationDbContext database,
        PageAuditSchedulingService scheduling,
        RecordingPageAuditQueue queue,
        Guid targetId,
        Guid endpointId,
        string endpointUrl)
    {
        var runId = await OpenQueuedRunAsync(database, targetId, endpointId, endpointUrl);
        await ExecuteSqlAsync(connectionString,
            $"UPDATE web_health.page_audit_run SET updated_at = now() - interval '30 minutes' "
            + $"WHERE id = '{runId}';");

        queue.Enqueued.Clear();
        (await scheduling.ReconcileAsync()).Should().Be(1);

        queue.Enqueued.Should().ContainSingle("the same run is asked for again, not a new one")
            .Which.Should().Be(runId);
        (await database.PageAuditRuns.AsNoTracking()
            .CountAsync(run => run.PageAuditTargetId == targetId
                && (run.Status == PageAuditRunStatuses.Queued
                    || run.Status == PageAuditRunStatuses.Running)))
            .Should().Be(1);

        await CloseRunAsync(connectionString, runId);
    }

    /// <summary>
    /// The snapshot makes the job un-steerable, and also makes it stale. An endpoint edited
    /// between queueing and execution re-derives its authorization for the new URL, so the
    /// eligibility check passes while the request still carries the old one - a host nobody
    /// authorized. The run must refuse rather than send it.
    /// </summary>
    private static async Task VerifyAStaleUrlIsNeverSentAsync(
        string connectionString,
        ApplicationDbContext database,
        PageAuditExecutionService execution,
        ScriptedPageAuditProvider provider,
        Guid targetId,
        Guid endpointId)
    {
        var runId = await OpenQueuedRunAsync(
            database, targetId, endpointId, "https://someone-elses-host.example.com/");
        provider.Respond(ScriptedPageAuditProvider.MixedResult());
        var callsBefore = provider.CallCount;

        var outcome = await execution.ExecuteAsync(runId);

        outcome.Status.Should().Be(PageAuditRunStatuses.Failed);
        outcome.FailureCategory.Should().Be(PageAuditFailureCategories.TargetRejected);
        provider.CallCount.Should().Be(callsBefore,
            "a URL the endpoint no longer has must never reach the provider");

        var run = await database.PageAuditRuns.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == runId);
        run.SafeDiagnostic.Should().Contain("changed after this run was queued");
    }

    /// <summary>
    /// A run with no attempts left can never be claimed again. Reconciliation therefore retires it
    /// instead of re-enqueueing work no worker will do - otherwise its target's single active-run
    /// slot is held against every later audit, forever.
    /// </summary>
    private static async Task VerifyASpentAttemptBudgetStopsReclaimAsync(
        string connectionString,
        ApplicationDbContext database,
        PageAuditExecutionService execution,
        ScriptedPageAuditProvider provider,
        PageAuditSchedulingService scheduling,
        RecordingPageAuditQueue queue,
        Guid targetId,
        Guid endpointId,
        string endpointUrl)
    {
        var runId = await OpenQueuedRunAsync(database, targetId, endpointId, endpointUrl);

        // A worker that died after spending the whole budget: Running, lease expired, no attempts
        // left. Before the ceiling was part of the claim this was reclaimed indefinitely.
        await ExecuteSqlAsync(connectionString,
            $"""
            UPDATE web_health.page_audit_run
            SET status = 'Running', lease_token = '{Guid.NewGuid()}',
                lease_expires_at = now() - interval '10 minutes', attempt_count = 3,
                updated_at = now() - interval '30 minutes'
            WHERE id = '{runId}';
            """);

        var callsBefore = provider.CallCount;
        provider.Respond(ScriptedPageAuditProvider.MixedResult());
        (await execution.ExecuteAsync(runId)).Status.Should().Be("NotClaimed");
        provider.CallCount.Should().Be(callsBefore,
            "a run past its attempt budget must not spend another request");

        queue.Enqueued.Clear();
        await scheduling.ReconcileAsync();

        queue.Enqueued.Should().NotContain(runId,
            "re-enqueueing a run nothing can claim would queue work no worker will do");
        var retired = await database.PageAuditRuns.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == runId);
        retired.Status.Should().Be(PageAuditRunStatuses.Failed);
        retired.FinishedAt.Should().NotBeNull("the target's active slot has to be released");
    }

    private static async Task<Guid> OpenQueuedRunAsync(
        ApplicationDbContext database,
        Guid targetId,
        Guid endpointId,
        string endpointUrl)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        database.PageAuditRuns.Add(new PageAuditRun
        {
            Id = runId,
            PageAuditTargetId = targetId,
            EndpointId = endpointId,
            Source = PageAuditSources.Scheduled,
            Status = PageAuditRunStatuses.Queued,
            RequestedUrl = endpointUrl,
            Provider = PageAuditProviders.PageSpeedInsights,
            Category = PageAuditCategories.Seo,
            Strategy = PageAuditStrategies.Mobile,
            Locale = "en-US",
            AttemptCount = 0,
            QueuedAt = now,
            UpdatedAt = now
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        return runId;
    }

    /// <summary>
    /// Closes the stage's own run so the next step starts from an empty active slot. The partial
    /// unique index allows one live run per target, and a stage that left one open would fail the
    /// next step with a uniqueness error rather than the rule it was testing.
    /// </summary>
    private static Task CloseRunAsync(string connectionString, Guid runId) =>
        ExecuteSqlAsync(connectionString,
            $"""
            UPDATE web_health.page_audit_run
            SET status = 'Cancelled', finished_at = now(), lease_token = NULL,
                lease_expires_at = NULL, updated_at = now()
            WHERE id = '{runId}' AND status IN ('Queued', 'Running');
            """);

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>A provider that answers however the stage tells it to, and counts its calls.</summary>
internal sealed class ScriptedPageAuditProvider : IPageAuditProvider
{
    private PageAuditProviderResult? _result;
    private PageAuditProviderException? _failure;

    public string ProviderName => PageAuditProviders.PageSpeedInsights;

    public int CallCount { get; private set; }

    public void Respond(PageAuditProviderResult result)
    {
        _result = result;
        _failure = null;
    }

    public void Fail(PageAuditProviderException failure)
    {
        _failure = failure;
        _result = null;
    }

    public Task<PageAuditProviderBatchResult> RunAsync(
        PageAuditRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (_failure is not null)
        {
            throw _failure;
        }

        var result = _result
            ?? throw new InvalidOperationException("The scripted provider was not given an answer.");
        return Task.FromResult(new PageAuditProviderBatchResult(
            request.Categories.ToDictionary(category => category, _ => result)));
    }

    /// <summary>
    /// One passed, one failed, one manual and one not-applicable audit — the four cases the
    /// counts on the page have to keep apart.
    /// </summary>
    public static PageAuditProviderResult MixedResult() => new(
        PageAuditProviders.PageSpeedInsights,
        "https://page-audit-exec.example.com/status",
        "https://page-audit-exec.example.com/status",
        DateTimeOffset.UtcNow,
        "11.4.0",
        0.8m,
        [
            new("document-title", "Document has a title", "The title gives an overview.",
                1m, PageAuditScoreDisplayModes.Binary, 10, "seo-content", null, null, null),
            new("meta-description", "Document does not have a meta description",
                "Meta descriptions may be included in search results.",
                0m, PageAuditScoreDisplayModes.Binary, 10, "seo-content", null, null, null),
            new("structured-data", "Structured data is valid", "Run the validator.",
                null, PageAuditScoreDisplayModes.Manual, 0, "seo-content", null, null, null),
            new("robots-txt", "robots.txt is valid", "A malformed robots.txt cannot be understood.",
                null, PageAuditScoreDisplayModes.NotApplicable, 10, "seo-crawl", null, null, null)
        ],
        [],
        null,
        null);
}

/// <summary>Records what was queued instead of reaching Hangfire.</summary>
internal sealed class RecordingPageAuditQueue : IPageAuditQueue
{
    public List<Guid> Enqueued { get; } = [];

    public List<(Guid RunId, TimeSpan Delay)> Scheduled { get; } = [];

    public void Enqueue(Guid runId) => Enqueued.Add(runId);

    public void Schedule(Guid runId, TimeSpan delay) => Scheduled.Add((runId, delay));
}
