using System.Diagnostics;
using FluentAssertions;
using Hangfire;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.SiteAnalysis;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class CrawlIsolationTests
{
    [Fact]
    public void CrawlRunJob_HasItsOwnQueueAndNeverRetriesARun()
    {
        var method = typeof(CrawlRunJob).GetMethod(nameof(CrawlRunJob.ExecuteAsync));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(QueueAttribute), false)
            .Cast<QueueAttribute>().Single().Queue.Should().Be("crawl");
        method.GetCustomAttributes(typeof(AutomaticRetryAttribute), false)
            .Cast<AutomaticRetryAttribute>().Single().Attempts.Should().Be(0,
                "re-running a crawl repeats every request against a target we do not own");
    }

    [Fact]
    public void CrawlQueue_IsNotTheQueueScheduledChecksUse() =>
        typeof(LogicalCheckJob).GetMethod(nameof(LogicalCheckJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(QueueAttribute), false)
            .Cast<QueueAttribute>().Single().Queue
            .Should().NotBe("crawl", "a crawl must not be able to occupy a monitoring worker");

    [Fact]
    public void CrawlRun_IsItsOwnDurableWorkKind() =>
        DurableWorkKinds.CrawlRun.Should()
            .NotBe(DurableWorkKinds.HttpCheck).And.NotBe(DurableWorkKinds.SslCheck);

    [Fact]
    public void DefaultOptions_LeaveAtLeastHalfTheSharedHttpBudgetForMonitoring()
    {
        var transport = new SafeHttpTransportOptions();
        var crawl = new CrawlSchedulingOptions();

        (crawl.WorkerCount * crawl.RequestConcurrency).Should()
            .BeLessOrEqualTo(transport.GlobalConcurrency / 2,
                "a saturated crawl must never hold the whole shared transport budget");
    }

    [Fact]
    public async Task ConcurrentRuns_ShareOneSiteAnalysisBudgetRatherThanOneEach()
    {
        var transportOptions = new SafeHttpTransportOptions();
        var budget = new SiteAnalysisRequestBudget(transportOptions);
        budget.Capacity.Should().Be(transportOptions.GlobalConcurrency / 2);

        var options = CrawlTestHarness.Options with { RequestConcurrency = budget.Capacity };
        var sites = Enumerable.Range(0, 3).Select(_ =>
        {
            var site = new FakeSiteTransport().Page(CrawlTestHarness.Seed, CrawlTestHarness.LinkTo(
                [.. Enumerable.Range(0, 60).Select(index => $"/page-{index}")]));
            foreach (var index in Enumerable.Range(0, 60))
            {
                site.Page($"https://site.test/page-{index}", CrawlTestHarness.LinkTo());
            }

            return site;
        }).ToArray();

        var inFlight = 0;
        var peak = 0;
        var gate = new Lock();
        foreach (var site in sites)
        {
            site.BeforeRespondAsync = _ =>
            {
                lock (gate)
                {
                    inFlight++;
                    peak = Math.Max(peak, inFlight);
                }

                return Task.Delay(15).ContinueWith(_ => { lock (gate) inFlight--; }, TaskScheduler.Default);
            };
        }

        await Task.WhenAll(sites.Select(site => CrawlTestHarness.RunAsync(
            site, CrawlTestHarness.Request(), options, budget: budget)));

        peak.Should().BeLessOrEqualTo(budget.Capacity,
            "three concurrent runs must not exceed the crawler's single shared share of the budget");
        budget.Available.Should().Be(budget.Capacity, "every slot must be returned");
    }

    [Fact]
    public async Task ASaturatedCrawl_HoldsNoMoreThanItsConfiguredRequestBudget()
    {
        var options = CrawlTestHarness.Options with { RequestConcurrency = 3 };
        var site = new FakeSiteTransport()
            .Page(CrawlTestHarness.Seed, CrawlTestHarness.LinkTo(
                [.. Enumerable.Range(0, 40).Select(index => $"/page-{index}")]));
        foreach (var index in Enumerable.Range(0, 40))
        {
            site.Page($"https://site.test/page-{index}", CrawlTestHarness.LinkTo());
        }

        site.BeforeRespondAsync = _ => Task.Delay(20);

        await CrawlTestHarness.RunAsync(site, CrawlTestHarness.Request(), options);

        site.MaxObservedConcurrency.Should().BeLessOrEqualTo(options.RequestConcurrency);
        site.MaxObservedConcurrency.Should().BeGreaterThan(1,
            "otherwise this test would pass on a crawler that never ran anything in parallel");
    }

    [Fact]
    public async Task ACrawlWhoseEveryRequestStalls_DoesNotDelayAConcurrentScheduledCheck()
    {
        var cadence = TimeSpan.FromMilliseconds(500);
        var options = CrawlTestHarness.Options with { RequestConcurrency = 2 };
        var site = new FakeSiteTransport()
            .Page(CrawlTestHarness.Seed, CrawlTestHarness.LinkTo(
                [.. Enumerable.Range(0, 20).Select(index => $"/slow-{index}")]));
        foreach (var index in Enumerable.Range(0, 20))
        {
            site.Page($"https://site.test/slow-{index}", CrawlTestHarness.LinkTo());
        }

        site.BeforeRespondAsync = _ => Task.Delay(200);

        using var stopCrawl = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var crawl = CrawlTestHarness.RunAsync(
            site, CrawlTestHarness.Request(), options, cancellationToken: stopCrawl.Token);

        await Task.Delay(150);
        var stopwatch = Stopwatch.StartNew();
        await SimulatedScheduledCheckAsync();
        stopwatch.Stop();

        await stopCrawl.CancelAsync();
        await crawl;

        stopwatch.Elapsed.Should().BeLessThan(cadence,
            "a saturated crawl must not push a scheduled check past its cadence");
    }

    private static async Task SimulatedScheduledCheckAsync()
    {
        var transport = new FakeSiteTransport().Page("https://monitored.test/", "<html></html>");
        await transport.SendAsync(new(Guid.NewGuid(), "https://monitored.test/", false));
    }
}
