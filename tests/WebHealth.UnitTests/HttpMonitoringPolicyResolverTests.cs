using FluentAssertions;
using WebHealth.Application.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class HttpMonitoringPolicyResolverTests
{
    private readonly HttpMonitoringPolicyResolver resolver = new();

    [Theory]
    [InlineData(true, 300)]
    [InlineData(false, 900)]
    public void DefaultsFollowEnvironmentAndFifteenSecondTimeout(bool production, int interval)
    {
        var result = resolver.Resolve(new(), production);

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Errors));
        result.Policy.Should().BeEquivalentTo(new ResolvedHttpMonitoringPolicy(
            interval, 15, 2, 2, 1500, 3000, [], null, "OrdinalIgnoreCase"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void TimeoutBoundsIncludeBothEndpoints(int seconds, bool valid)
    {
        var result = resolver.Resolve(new() { TimeoutSeconds = seconds, WarningThresholdMs = 1, CriticalThresholdMs = 1 }, false);

        result.Succeeded.Should().Be(valid, string.Join(" ", result.Errors));
        if (!valid) result.Errors.Should().Contain(error => error.Field == "TimeoutSeconds");
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(10, 10, true)]
    [InlineData(11, 10, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 11, false)]
    public void ConfirmationCountsAreBoundedIndependently(int failures, int recoveries, bool valid)
    {
        var result = resolver.Resolve(new() { FailureConfirmationCount = failures, RecoveryConfirmationCount = recoveries }, false);

        result.Succeeded.Should().Be(valid, string.Join(" ", result.Errors));
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1500, 1499, false)]
    [InlineData(1500, 15000, true)]
    [InlineData(1500, 15001, false)]
    public void ThresholdsRespectOrderingAndTimeout(int warning, int critical, bool valid)
    {
        var result = resolver.Resolve(new() { WarningThresholdMs = warning, CriticalThresholdMs = critical }, false);

        result.Succeeded.Should().Be(valid, string.Join(" ", result.Errors));
    }

    [Fact]
    public void AdditionalStatusesAreCanonicalButNeverIncludeServerErrors()
    {
        var accepted = resolver.Resolve(new() { AdditionalAcceptedStatusCodes = [499, 300, 499] }, false);
        accepted.Succeeded.Should().BeTrue(string.Join(" ", accepted.Errors));
        accepted.Policy!.AdditionalAcceptedStatusCodes.Should().Equal(300, 499);
        var forbidden = resolver.Resolve(new() { AdditionalAcceptedStatusCodes = [500] }, false);
        forbidden.Errors.Should().Contain(error => error.Field == "AdditionalAcceptedStatusCodes");
        forbidden.Policy.Should().BeNull();
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void AdditionalStatusesHaveABoundedDistinctCount(int count, bool valid)
    {
        var result = resolver.Resolve(new() { AdditionalAcceptedStatusCodes = Enumerable.Range(300, count).ToArray() }, false);

        result.Succeeded.Should().Be(valid, string.Join(" ", result.Errors));
    }

    [Theory]
    [InlineData(500, "Ordinal", true)]
    [InlineData(501, "Ordinal", false)]
    [InlineData(1, "CurrentCulture", false)]
    public void MarkerValidationIsBoundedAndDoesNotEchoContent(int length, string comparison, bool valid)
    {
        var marker = new string('x', length);
        var result = resolver.Resolve(new() { RequiredContentMarker = marker, ContentMarkerComparison = comparison }, false);

        result.Succeeded.Should().Be(valid, string.Join(" ", result.Errors));
        result.Errors.Select(error => error.Message).Should().NotContain(message => message.Contains(marker, StringComparison.Ordinal));
    }
}
