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
        var targetId = await SeedTargetAsync(database, endpointId);

        await VerifyAnUnauditedEndpointReadsAsConfiguredButUnmeasuredAsync(reader, access, endpointId);
        var firstRunId = await VerifyTheFirstRunHasNothingToCompareAgainstAsync(
            database, reader, access, targetId, endpointId);
        await VerifyCountsKeepEveryAuditStatusApartAsync(database, reader, access, endpointId, firstRunId);
        await VerifyASecondRunComparesAgainstTheFirstAsync(
            database, reader, access, targetId, endpointId);
        await VerifyAMajorVersionChangeIsLabelledAsync(database, reader, access, targetId, endpointId);
        await VerifyAFailedRunIsNotComparedAsync(database, reader, access, targetId, endpointId);
        await VerifyAnotherClientsEndpointIsNotReadableAsync(database, reader, endpointId);
    }

    private static async Task<Guid> SeedTargetAsync(ApplicationDbContext database, Guid endpointId)
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
        var summary = await reader.GetEndpointSummaryAsync(endpointId, null, access);

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
            database, targetId, endpointId, 0.82m, "11.4.0", DateTimeOffset.UtcNow.AddHours(-2));

        var summary = await reader.GetEndpointSummaryAsync(endpointId, null, access);

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

        var summary = await reader.GetEndpointSummaryAsync(endpointId, null, access);

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
            database, targetId, endpointId, 0.91m, "11.4.0", DateTimeOffset.UtcNow.AddHours(-1));

        var summary = await reader.GetEndpointSummaryAsync(endpointId, null, access);

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
            database, targetId, endpointId, 0.75m, "12.0.1", DateTimeOffset.UtcNow.AddMinutes(-30));

        var summary = await reader.GetEndpointSummaryAsync(endpointId, null, access);

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

        var summary = await reader.GetEndpointSummaryAsync(endpointId, null, access);

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
        var selected = await reader.GetEndpointSummaryAsync(endpointId, lastScored, access);
        selected!.Comparison.PreviousRunId.Should().NotBeNull();
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

        (await reader.GetEndpointSummaryAsync(endpointId, null, viewerWithNoGrants))
            .Should().BeNull("a Viewer with no grant over this endpoint may not read its audits");
        (await reader.ListRunsAsync(endpointId, 20, viewerWithNoGrants)).Should().BeEmpty();
    }

    private static async Task<Guid> AddCompletedRunAsync(
        ApplicationDbContext database,
        Guid targetId,
        Guid endpointId,
        decimal rawScore,
        string lighthouseVersion,
        DateTimeOffset finishedAt)
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
            Category = PageAuditCategories.Seo,
            Strategy = PageAuditStrategies.Mobile,
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
