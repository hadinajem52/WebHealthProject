using FluentAssertions;
using Hangfire;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.PageAudits;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// A PageSpeed audit is a call to somebody else's infrastructure that can take a minute and a
/// half. The isolation claim is proved here rather than left to design intent, because a page
/// audit that starved availability monitoring would be a silent failure: checks would simply
/// report late.
/// </summary>
public sealed class PageAuditIsolationTests
{
    [Fact]
    public void PageAuditRunJob_RunsOnItsOwnQueue() =>
        QueueOf(typeof(PageAuditRunJob), nameof(PageAuditRunJob.ExecuteAsync))
            .Should().Be("page-audits");

    [Fact]
    public void PageAuditDispatchJob_RunsOnTheSameIsolatedQueueAsTheRunsItOpens() =>
        QueueOf(typeof(PageAuditDispatchJob), nameof(PageAuditDispatchJob.DispatchAsync))
            .Should().Be("page-audits");

    [Fact]
    public void PageAuditQueue_IsNotTheQueueScheduledChecksUse() =>
        QueueOf(typeof(LogicalCheckJob), nameof(LogicalCheckJob.ExecuteAsync))
            .Should().NotBe("page-audits",
                "a ninety-second call to Google must not be able to occupy a monitoring worker");

    [Fact]
    public void PageAuditQueue_IsNotTheQueueCrawlsUse() =>
        QueueOf(typeof(CrawlRunJob), nameof(CrawlRunJob.ExecuteAsync))
            .Should().NotBe("page-audits",
                "the two long-running features must not compete for one another's workers");

    /// <summary>
    /// The application counts attempts itself, in <c>attempt_count</c>. Hangfire retrying as well
    /// would mean two mechanisms disagreeing about how many times we have already asked Google to
    /// load somebody's page, and only one of those counts is stored beside the run.
    /// </summary>
    [Fact]
    public void PageAuditRunJob_LeavesRetryToTheApplicationsOwnAttemptBudget() =>
        typeof(PageAuditRunJob).GetMethod(nameof(PageAuditRunJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), false)
            .Cast<AutomaticRetryAttribute>().Single().Attempts.Should().Be(0);

    [Fact]
    public void DefaultOptions_KeepTheAuditWorkerPoolSmall()
    {
        var options = new PageAuditSchedulingOptions();

        options.Enabled.Should().BeFalse("the feature ships off until somebody configures a key");
        options.WorkerCount.Should().Be(1,
            "concurrency here buys latency at the cost of spending somebody else's quota faster");
    }

    /// <summary>
    /// A claim has to outlive the call it protects. A lease shorter than the provider timeout
    /// would let a second worker reclaim a run while the first is still waiting on Google, and
    /// both would then audit the same page.
    /// </summary>
    [Fact]
    public void DefaultOptions_HoldALeaseForLongerThanTheProviderIsGivenToAnswer() =>
        new PageAuditSchedulingOptions().LeaseDuration
            .Should().BeGreaterThan(new PageSpeedInsightsOptions().RequestTimeout);

    [Fact]
    public void DefaultOptions_BoundTheAttemptsOneRunMaySpend() =>
        new PageAuditSchedulingOptions().MaximumAttempts.Should().BeInRange(1, 5,
            "every attempt is another request against somebody else's site and quota");

    /// <summary>
    /// The service origin is a constant. As configuration it would be a way to point a client
    /// holding our API key at any host a settings file names.
    /// </summary>
    [Fact]
    public void ServiceOrigin_IsAConstantRatherThanAConfigurableSetting()
    {
        PageSpeedInsightsProvider.ServiceOrigin.Should().Be("https://pagespeedonline.googleapis.com/");
        typeof(PageSpeedInsightsOptions).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(name =>
                name.Contains("Url", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Host", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Endpoint", StringComparison.OrdinalIgnoreCase)
                || name.Contains("BaseAddress", StringComparison.OrdinalIgnoreCase));
    }

    private static string QueueOf(Type jobType, string methodName) =>
        jobType.GetMethod(methodName)!
            .GetCustomAttributes(typeof(QueueAttribute), false)
            .Cast<QueueAttribute>().Single().Queue;
}
