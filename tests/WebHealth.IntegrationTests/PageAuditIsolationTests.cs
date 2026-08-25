using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebHealth.Infrastructure;
using Hangfire;
using WebHealth.Infrastructure.PageAudits;
using Xunit;

namespace WebHealth.IntegrationTests;

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
    public void PageAuditRunJob_LeavesRetryToTheApplicationsOwnAttemptBudget() =>
        typeof(PageAuditRunJob).GetMethod(nameof(PageAuditRunJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), false)
            .Cast<AutomaticRetryAttribute>().Single().Attempts.Should().Be(0);

    [Fact]
    public void DefaultOptions_KeepTheAuditWorkerPoolSmall()
    {
        var options = new PageAuditSchedulingOptions();

        options.Enabled.Should().BeFalse("the feature ships off until somebody configures a key");
        options.WorkerCount.Should().Be(2,
            "one run now opens a batch per strategy, so a single worker made the mobile batch wait "
            + "out the desktop one and doubled its observed latency for no gain; two workers let "
            + "the pair overlap while still capping what one endpoint spends of somebody else's quota");
    }

    [Fact]
    public void DefaultOptions_HoldALeaseForLongerThanTheProviderIsGivenToAnswer() =>
        new PageAuditSchedulingOptions().LeaseDuration
            .Should().BeGreaterThan(new PageSpeedInsightsOptions().RequestTimeout);

    [Fact]
    public void DefaultOptions_BoundTheAttemptsOneRunMaySpend() =>
        new PageAuditSchedulingOptions().MaximumAttempts.Should().BeInRange(1, 5,
            "every attempt is another request against somebody else's site and quota");

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

    [Fact]
    public void EnablingOnlyPageAudits_StillBuildsAStartableServiceProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = "Host=127.0.0.1;Port=1;Database=none;Username=none",
            ["Monitoring:Scheduling:Enabled"] = "false",
            ["Notifications:Scheduling:Enabled"] = "false",
            ["Maintenance:Scheduling:Enabled"] = "false",
            ["Seo:Scheduling:Enabled"] = "false",
            ["Crawling:Scheduling:Enabled"] = "false",
            ["PageAudits:Scheduling:Enabled"] = "true",
            ["PageAudits:PageSpeedInsights:ApiKey"] = "configured"
        }).Build();

        var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration);

        var provider = services.BuildServiceProvider();

        var act = () => provider.GetServices<IHostedService>().ToArray();

        act.Should().NotThrow();
    }

    private static string QueueOf(Type jobType, string methodName) =>
        jobType.GetMethod(methodName)!
            .GetCustomAttributes(typeof(QueueAttribute), false)
            .Cast<QueueAttribute>().Single().Queue;
}
