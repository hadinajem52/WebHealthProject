using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.PageAudits;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

/// <summary>
/// What the PageSpeed page reads: the selected run, the audits behind it, the counts that keep
/// manual and not-applicable apart from passed, and the comparison against the run before it.
/// </summary>
/// <remarks>
/// The stage seeds its own completed runs rather than driving the executor again. It is asserting
/// the read model, and building each fixture score directly is what lets it state the exact delta
/// and version pairing each case is about.
/// </remarks>
internal static class PageAuditReaderAssertions
{
    public static async Task VerifyAsync(
        ApplicationDbContext database,
        IPageAuditReader reader,
        Guid administratorId,
        Guid endpointId)
    {
        var access = new RegistryAccessContext(administratorId, [ApplicationRoles.Administrator]);
        var targetId = await SeedTargetAsync(database, endpointId, PageAuditStrategies.Mobile);

        await VerifyAnUnauditedEndpointReadsAsConfiguredButUnmeasuredAsync(reader, access, endpointId);
        var firstRunId = await VerifyTheFirstRunHasNothingToCompareAgainstAsync(
            database, reader, access, targetId, endpointId);
        await VerifyCountsKeepEveryAuditStatusApartAsync(database, reader, access, endpointId, firstRunId);
        await VerifyASecondRunComparesAgainstTheFirstAsync(
            database, reader, access, targetId, endpointId);
        await VerifyAMajorVersionChangeIsLabelledAsync(database, reader, access, targetId, endpointId);
        await VerifyAFailedRunIsNotComparedAsync(database, reader, access, targetId, endpointId);
        await VerifyTheTwoFormFactorsReadApartAsync(database, reader, access, endpointId);
        await VerifyCategoriesReadApartAsync(database, reader, access, endpointId);
        await VerifyConfigurationIsEndpointLevelAsync(database, reader, access, endpointId);
        await VerifyAnotherClientsEndpointIsNotReadableAsync(database, reader, endpointId);
    }

    private static async Task<Guid> SeedTargetAsync(
        ApplicationDbContext database,
        Guid endpointId,
        string strategy,
        string category = PageAuditCategories.Seo)
    {
        var now = DateTimeOffset.UtcNow;
        var targetId = Guid.NewGuid();
        database.PageAuditTargets.Add(new PageAuditTarget
        {
            Id = targetId,
            EndpointId = endpointId,
            Provider = PageAuditProviders.PageSpeedInsights,
            Category = category,
            Strategy = strategy,
            IsEnabled = true,
            SchedulingEnabled = true,
            IntervalSeconds = 86400,
            ScheduleAnchor = now,
            NextDueAt = now.AddDays(1),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        return targetId;
    }

    /// <summary>
    /// Configured and enabled, with no score. The page has to tell that apart from a disabled
    /// endpoint, because one is waiting for a first audit and the other never asked for any.
    /// </summary>
    private static async Task VerifyAnUnauditedEndpointReadsAsConfiguredButUnmeasuredAsync(
        IPageAuditReader reader,
        RegistryAccessContext access,
        Guid endpointId)
    {
        var summary = await reader.GetEndpointSummaryAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Mobile, null, access);

        summary.Should().NotBeNull();
        summary!.IsConfigured.Should().BeTrue();
        summary.IsEnabled.Should().BeTrue();
        summary.SchedulingEnabled.Should().BeTrue();
        summary.IntervalHours.Should().Be(24);
        summary.LatestRun.Should().BeNull();
        summary.Counts.Total.Should().Be(0);
        summary.Comparison.CurrentRunId.Should().BeNull();
    }

    private static async Task<Guid> VerifyTheFirstRunHasNothingToCompareAgainstAsync(
        ApplicationDbContext database,
        IPageAuditReader reader,
        RegistryAccessContext access,
        Guid targetId,
        Guid endpointId)
    {
        var runId = await AddCompletedRunAsync(
            database, targetId, endpointId, PageAuditStrategies.Mobile, 0.82m, "11.4.0",
            DateTimeOffset.UtcNow.AddHours(-2));

        var summary = await reader.GetEndpointSummaryAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Mobile, null, access);

        summary!.LatestRun!.RunId.Should().Be(runId);
        summary.LatestRun.Score.Should().Be(82, "0.82 rounds to 82 on the documented rule");
        summary.LatestRun.HasScore.Should().BeTrue();
        summary.Comparison.CurrentRunId.Should().Be(runId);
        summary.Comparison.PreviousRunId.Should().BeNull("there is nothing earlier to compare with");
        summary.Comparison.Delta.Should().BeNull();
        return runId;
    }

    /// <summary>
    /// The counts are what the page shows above the audit sections, and they must never fold a
    /// manual or not-applicable audit into the passed total: neither is a check the page passed.
    /// </summary>
    private static async Task VerifyCountsKeepEveryAuditStatusApartAsync(
        ApplicationDbContext database,
        IPageAuditReader reader,
        RegistryAccessContext access,
        Guid endpointId,
        Guid runId)
    {
        await AddItemsAsync(database, runId,
        [
            ("document-title", PageAuditItemStatuses.Passed, 1m, 10),
            ("viewport", PageAuditItemStatuses.Passed, 1m, 10),
            ("meta-description", PageAuditItemStatuses.Failed, 0m, 30),
            ("structured-data", PageAuditItemStatuses.Manual, null, 0),
            ("robots-txt", PageAuditItemStatuses.NotApplicable, null, 10),
            ("font-size", PageAuditItemStatuses.Informative, null, 0),
            ("canonical", PageAuditItemStatuses.Error, null, 10)
        ]);

        var summary = await reader.GetEndpointSummaryAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Mobile, null, access);

        summary!.Counts.Passed.Should().Be(2);
        summary.Counts.Failed.Should().Be(1);
        summary.Counts.Manual.Should().Be(1, "a manual check is not a pass and not a failure");
        summary.Counts.NotApplicable.Should().Be(1);
        summary.Counts.Informative.Should().Be(1);
        summary.Counts.Error.Should().Be(1, "an audit that could not run is not a finding");
        summary.Counts.Total.Should().Be(7);

        var items = await reader.ListAuditItemsAsync(runId, access);
        items.Should().HaveCount(7);
        items[0].AuditId.Should().Be("meta-description",
            "the heaviest audit is read first because it moved the score most");
    }

    private static async Task VerifyASecondRunComparesAgainstTheFirstAsync(
        ApplicationDbContext database,
        IPageAuditReader reader,
        RegistryAccessContext access,
        Guid targetId,
        Guid endpointId)
    {
        var runId = await AddCompletedRunAsync(
            database, targetId, endpointId, PageAuditStrategies.Mobile, 0.91m, "11.4.0",
            DateTimeOffset.UtcNow.AddHours(-1));

        var summary = await reader.GetEndpointSummaryAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Mobile, null, access);

        summary!.LatestRun!.RunId.Should().Be(runId);
        summary.Comparison.CurrentScore.Should().Be(91);
        summary.Comparison.PreviousScore.Should().Be(82);
        summary.Comparison.Delta.Should().Be(9);
        summary.Comparison.Comparability.Should().Be(PageAuditComparability.Comparable);
        summary.Comparison.SpansAVersionChange.Should().BeFalse();
    }

    /// <summary>
    /// A major-version change can add, remove or redefine audits, so the delta is still shown and
    /// still labelled. Hiding it would lose real information; presenting it silently would report
    /// a change in the tool as a change in the page.
    /// </summary>
    private static async Task VerifyAMajorVersionChangeIsLabelledAsync(
        ApplicationDbContext database,
        IPageAuditReader reader,
        RegistryAccessContext access,
        Guid targetId,
        Guid endpointId)
    {
        var runId = await AddCompletedRunAsync(
            database, targetId, endpointId, PageAuditStrategies.Mobile, 0.75m, "12.0.1",
            DateTimeOffset.UtcNow.AddMinutes(-30));

        var summary = await reader.GetEndpointSummaryAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Mobile, null, access);

        summary!.LatestRun!.RunId.Should().Be(runId);
        summary.Comparison.PreviousScore.Should().Be(91);
        summary.Comparison.Delta.Should().Be(-16, "the delta is still reported");
        summary.Comparison.Comparability.Should().Be(
            PageAuditComparability.LighthouseVersionChanged);
        summary.Comparison.SpansAVersionChange.Should().BeTrue();
    }

    /// <summary>
    /// A failed run has no score. Treating its absence as a change would report a Google outage
    /// as a collapse in the page's SEO, which is the one reading this feature must never produce.
    /// </summary>
    private static async Task VerifyAFailedRunIsNotComparedAsync(
        ApplicationDbContext database,
        IPageAuditReader reader,
        RegistryAccessContext access,
        Guid targetId,
        Guid endpointId)
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        database.PageAuditRuns.Add(new PageAuditRun
        {
            Id = runId,
            PageAuditTargetId = targetId,
            EndpointId = endpointId,
            Source = PageAuditSources.Scheduled,
            Status = PageAuditRunStatuses.Failed,
            RequestedUrl = "https://page-audit-reader.example.com/status",
            Provider = PageAuditProviders.PageSpeedInsights,
            Category = PageAuditCategories.Seo,
            Strategy = PageAuditStrategies.Mobile,
            Locale = "en-US",
            FailureCategory = PageAuditFailureCategories.ProviderUnavailable,
            SafeDiagnostic = "The provider is unavailable: HTTP 503.",
            AttemptCount = 3,
            QueuedAt = now.AddMinutes(-10),
            FinishedAt = now,
            UpdatedAt = now
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var summary = await reader.GetEndpointSummaryAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Mobile, null, access);

        summary!.LatestRun!.RunId.Should().Be(runId, "the newest run is shown whatever its status");
        summary.LatestRun.HasScore.Should().BeFalse();
        summary.LatestRun.FailureCategory.Should().Be(PageAuditFailureCategories.ProviderUnavailable);
        summary.Comparison.Should().Be(PageAuditComparison.None,
            "a run with no score is not one side of a comparison");

        // Selecting the last scored run explicitly still compares, so a failure does not hide the
        // history behind it.
        var lastScored = await database.PageAuditRuns.AsNoTracking()
            .Where(run => run.PageAuditTargetId == targetId && run.RawScore != null)
            .OrderByDescending(run => run.FinishedAt)
            .ThenByDescending(run => run.Id)
            .Select(run => run.Id)
            .FirstAsync();
        var selected = await reader.GetEndpointSummaryAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Mobile, lastScored, access);
        selected!.Comparison.PreviousRunId.Should().NotBeNull();
    }

    /// <summary>
    /// Mobile and desktop are two measurements of the same page, not two views of one result.
    /// Reading either must never pick up the other's runs: a desktop score shown under mobile
    /// would report a form factor the page never audited that way.
    /// </summary>
    private static async Task VerifyTheTwoFormFactorsReadApartAsync(
        ApplicationDbContext database,
        IPageAuditReader reader,
        RegistryAccessContext access,
        Guid endpointId)
    {
        var desktopTargetId = await SeedTargetAsync(
            database, endpointId, PageAuditStrategies.Desktop);
        var desktopRunId = await AddCompletedRunAsync(
            database, desktopTargetId, endpointId, PageAuditStrategies.Desktop, 0.64m, "11.4.0",
            DateTimeOffset.UtcNow.AddMinutes(-5));

        var desktop = await reader.GetEndpointSummaryAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Desktop, null, access);

        desktop!.Strategy.Should().Be(PageAuditStrategies.Desktop);
        desktop.LatestRun!.RunId.Should().Be(desktopRunId);
        desktop.LatestRun.Score.Should().Be(64);
        desktop.Comparison.PreviousRunId.Should().BeNull(
            "the mobile history is a different measurement, not this run's previous score");

        var mobile = await reader.GetEndpointSummaryAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Mobile, null, access);

        mobile!.Strategy.Should().Be(PageAuditStrategies.Mobile);
        mobile.LatestRun!.RunId.Should().NotBe(desktopRunId,
            "a desktop run is never the endpoint's newest mobile run, however recent it is");

        var desktopRuns = await reader.ListRunsAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Desktop, 20, access);

        desktopRuns.Should().ContainSingle(
            "one desktop run has been recorded, against several mobile ones")
            .Which.Strategy.Should().Be(PageAuditStrategies.Desktop);

        // The score a reading carries is its own form factor's. A desktop number surfacing while
        // mobile is selected would attribute one page's measurement to the other.
        desktop.LatestRun!.Score.Should().Be(64);
        mobile.LatestRun!.Score.Should().BeNull(
            "the newest mobile run is the failed one, which produced no score of its own");
    }

    /// <summary>
    /// Auditing is one setting on the endpoint. A form factor whose own target row is missing -
    /// a database that has not applied the desktop migration yet - must still read as configured
    /// and enabled, because the alternative tells an operator to switch on something that is
    /// already on.
    /// </summary>
    private static async Task VerifyConfigurationIsEndpointLevelAsync(
        ApplicationDbContext database,
        IPageAuditReader reader,
        RegistryAccessContext access,
        Guid endpointId)
    {
        var desktopTargetIds = await database.PageAuditTargets.AsNoTracking()
            .Where(target => target.EndpointId == endpointId
                && target.Strategy == PageAuditStrategies.Desktop)
            .Select(target => target.Id)
            .ToArrayAsync();
        await database.PageAuditRuns
            .Where(run => desktopTargetIds.Contains(run.PageAuditTargetId))
            .ExecuteDeleteAsync();
        await database.PageAuditTargets
            .Where(target => desktopTargetIds.Contains(target.Id))
            .ExecuteDeleteAsync();
        database.ChangeTracker.Clear();

        var desktop = await reader.GetEndpointSummaryAsync(
            endpointId, PageAuditCategories.Seo, PageAuditStrategies.Desktop, null, access);

        desktop!.IsConfigured.Should().BeTrue(
            "PageSpeed is configured on the endpoint, not on one form factor");
        desktop.IsEnabled.Should().BeTrue();
        desktop.SchedulingEnabled.Should().BeTrue();
        desktop.IntervalHours.Should().Be(24);
        desktop.NextDueAt.Should().BeNull("this form factor has no row to be due from");
        desktop.LatestRun.Should().BeNull();
    }

    private static async Task VerifyCategoriesReadApartAsync(
        ApplicationDbContext database,
        IPageAuditReader reader,
        RegistryAccessContext access,
        Guid endpointId)
    {
        var targetId = await SeedTargetAsync(
            database,
            endpointId,
            PageAuditStrategies.Mobile,
            PageAuditCategories.Performance);
        var runId = await AddCompletedRunAsync(
            database,
            targetId,
            endpointId,
            PageAuditStrategies.Mobile,
            0.71m,
            "12.0.1",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            PageAuditCategories.Performance);
        var latestRunId = await AddCompletedRunAsync(
            database,
            targetId,
            endpointId,
            PageAuditStrategies.Mobile,
            0.91m,
            "12.0.1",
            DateTimeOffset.UtcNow,
            PageAuditCategories.Performance);

        var performance = await reader.GetEndpointSummaryAsync(
            endpointId,
            PageAuditCategories.Performance,
            PageAuditStrategies.Mobile,
            null,
            access);
        var seo = await reader.GetEndpointSummaryAsync(
            endpointId,
            PageAuditCategories.Seo,
            PageAuditStrategies.Mobile,
            null,
            access);
        var accessibility = await reader.GetEndpointSummaryAsync(
            endpointId,
            PageAuditCategories.Accessibility,
            PageAuditStrategies.Mobile,
            null,
            access);
        var historical = await reader.GetEndpointSummaryAsync(
            endpointId,
            PageAuditCategories.Performance,
            PageAuditStrategies.Mobile,
            runId,
            access);
        var categoryCards = await reader.GetLatestCategorySummariesAsync(
            endpointId,
            PageAuditStrategies.Mobile,
            access);

        performance!.Category.Should().Be(PageAuditCategories.Performance);
        performance.LatestRun!.RunId.Should().Be(latestRunId);
        performance.LatestRun.Score.Should().Be(91);
        historical!.LatestRun!.RunId.Should().Be(runId);
        categoryCards!.Single(card => card.Category == PageAuditCategories.Performance)
            .LatestRun!.RunId.Should().Be(latestRunId);
        seo!.LatestRun!.RunId.Should().NotBe(runId);
        accessibility!.IsConfigured.Should().BeTrue();
        accessibility.IsEnabled.Should().BeTrue();
        accessibility.LatestRun.Should().BeNull();
    }

    /// <summary>
    /// Visibility is composed into the query, so an endpoint the requester may not see reads as
    /// absent. The controller turns that into Not Found; answering Forbidden would confirm it
    /// exists, which is itself a disclosure.
    /// </summary>
    private static async Task VerifyAnotherClientsEndpointIsNotReadableAsync(
        ApplicationDbContext database,
        IPageAuditReader reader,
        Guid endpointId)
    {
        var strangerId = await database.Users.AsNoTracking()
            .Where(user => !user.IsDisabled)
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .FirstAsync();
        var viewerWithNoGrants = new RegistryAccessContext(strangerId, [ApplicationRoles.Viewer]);

        (await reader.GetEndpointSummaryAsync(
                endpointId, PageAuditCategories.Seo, PageAuditStrategies.Mobile, null, viewerWithNoGrants))
            .Should().BeNull("a Viewer with no grant over this endpoint may not read its audits");
        (await reader.GetLatestCategorySummariesAsync(
                endpointId, PageAuditStrategies.Mobile, viewerWithNoGrants))
            .Should().BeNull("category cards must enforce the same endpoint visibility scope");
        (await reader.ListRunsAsync(endpointId, PageAuditCategories.Seo, PageAuditStrategies.Mobile, 20, viewerWithNoGrants))
            .Should().BeEmpty();
    }

    private static async Task<Guid> AddCompletedRunAsync(
        ApplicationDbContext database,
        Guid targetId,
        Guid endpointId,
        string strategy,
        decimal rawScore,
        string lighthouseVersion,
        DateTimeOffset finishedAt,
        string category = PageAuditCategories.Seo)
    {
        var runId = Guid.NewGuid();
        database.PageAuditRuns.Add(new PageAuditRun
        {
            Id = runId,
            PageAuditTargetId = targetId,
            EndpointId = endpointId,
            Source = PageAuditSources.Scheduled,
            Status = PageAuditRunStatuses.Completed,
            RequestedUrl = "https://page-audit-reader.example.com/status",
            FinalUrl = "https://page-audit-reader.example.com/status",
            RawScore = rawScore,
            Provider = PageAuditProviders.PageSpeedInsights,
            Category = category,
            Strategy = strategy,
            Locale = "en-US",
            LighthouseVersion = lighthouseVersion,
            AttemptCount = 1,
            QueuedAt = finishedAt.AddMinutes(-1),
            AnalysisAt = finishedAt,
            FinishedAt = finishedAt,
            UpdatedAt = finishedAt
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        return runId;
    }

    private static async Task AddItemsAsync(
        ApplicationDbContext database,
        Guid runId,
        IReadOnlyList<(string AuditId, string Status, decimal? Score, double Weight)> items)
    {
        // Added through the DbSet rather than through a loaded navigation collection: keys here
        // are client-generated, so EF would attach an entity added to a collection as an existing
        // row and emit an UPDATE against an id that was never inserted.
        foreach (var (auditId, status, score, weight) in items)
        {
            database.PageAuditItems.Add(new PageAuditItem
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                AuditId = auditId,
                Status = status,
                Score = score,
                ScoreDisplayMode = PageAuditScoreDisplayModes.Binary,
                Weight = weight,
                Title = auditId
            });
        }

        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
    }
}
