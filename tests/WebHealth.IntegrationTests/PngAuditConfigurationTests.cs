using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.Monitoring;
using WebHealth.Application.PngAudits;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.PngAudits;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PngAuditConfigurationTests
{
    [Fact]
    public void DefaultOptions_MatchThePhaseOneSafetyContract()
    {
        var options = new PngAuditOptions();

        options.Enabled.Should().BeFalse();
        options.WorkerCount.Should().Be(1);
        options.MaxPageBytes.Should().Be(1024 * 1024);
        options.MaxImageBytes.Should().Be(SafeHttpTransportDefaults.AbsoluteMaxResponseBodyBytes);
        options.ImageFetchConcurrency.Should().Be(1);
        options.ImageDecodeConcurrency.Should().Be(1);
        options.RequestsPerSecondPerHost.Should().Be(1);
        options.MinSavingsPercent.Should().Be(10);
        options.MinSavingsBytes.Should().Be(4096);
    }

    [Fact]
    public void AddInfrastructure_RejectsPngImageLimitAboveTheTransportCeiling()
    {
        var values = DisabledInfrastructure();
        values["PngAudits:MaxImageBytes"] =
            (SafeHttpTransportDefaults.AbsoluteMaxResponseBodyBytes + 1).ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var act = () => new ServiceCollection().AddLogging().AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("PNG audit options are outside their safe bounds.");
    }

    [Fact]
    public void AddInfrastructure_EnablesPngAuditsWithoutEnablingBrokenLinkCrawling()
    {
        var values = DisabledInfrastructure();
        values["PngAudits:Enabled"] = "true";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration);

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IPngAuditRunQueue)
            && descriptor.ImplementationType == typeof(HangfirePngAuditRunQueue));
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IPngAuditRunner)
            && descriptor.ImplementationType == typeof(PngAuditRunner));
    }

    [Fact]
    public void AddInfrastructure_RegistersTheConfiguredRecommendationThresholds()
    {
        var values = DisabledInfrastructure();
        values["PngAudits:MinSavingsPercent"] = "15";
        values["PngAudits:MinSavingsBytes"] = "1024";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var thresholds = provider.GetRequiredService<PngRecommendationThresholds>();
        var analyzer = provider.GetRequiredService<IPngImageAnalyzer>();

        thresholds.MinSavingsPercent.Should().Be(15);
        thresholds.MinSavingsBytes.Should().Be(1024);
        analyzer.Should().BeOfType<PngImageAnalyzer>();
    }

    private static Dictionary<string, string?> DisabledInfrastructure() => new()
    {
        ["ConnectionStrings:WebHealth"] = "Host=127.0.0.1;Port=1;Database=none;Username=none",
        ["Monitoring:Scheduling:Enabled"] = "false",
        ["Notifications:Scheduling:Enabled"] = "false",
        ["Maintenance:Scheduling:Enabled"] = "false",
        ["Seo:Scheduling:Enabled"] = "false",
        ["Crawling:Scheduling:Enabled"] = "false",
        ["PageAudits:Scheduling:Enabled"] = "false",
        ["PngAudits:Enabled"] = "false"
    };
}
