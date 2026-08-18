using FluentAssertions;
using WebHealth.Application.Registry;
using WebHealth.Domain.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// BR-P02's override half: an endpoint may replace the documented response-time budget, and
/// the values it may replace it with are constrained so the resulting bands stay reachable.
/// </summary>
public sealed class ResponseThresholdOverrideTests
{
    [Fact]
    public void NoSubmittedValues_UseTheDocumentedDefaults()
    {
        var decision = ResponseThresholdOverride.Decide(null, null);

        decision.Error.Should().BeNull();
        decision.Thresholds.Should().Be(ResponseTimeThresholds.Default);
        decision.Thresholds.WarningMs.Should().Be(1_500);
        decision.Thresholds.CriticalMs.Should().Be(3_000);
    }

    [Fact]
    public void BothSubmittedValues_BecomeTheEndpointOverride()
    {
        var decision = ResponseThresholdOverride.Decide(600, 1_200);

        decision.Error.Should().BeNull();
        decision.Thresholds.Should().Be(new ResponseTimeThresholds(600, 1_200));
    }

    [Theory]
    [InlineData(600, null)]
    [InlineData(null, 1_200)]
    public void OneSubmittedValueWithoutTheOther_IsRejected(int? warning, int? critical)
    {
        // Half an override would leave a warning threshold that can sit above an unchanged
        // critical one, producing a band nothing can fall into.
        ResponseThresholdOverride.Decide(warning, critical).Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ACriticalThresholdBelowTheWarningThreshold_IsRejected()
    {
        ResponseThresholdOverride.Decide(2_000, 1_000).Error.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(0, 1_000)]
    [InlineData(-1, 1_000)]
    [InlineData(1_000, ResponseThresholdOverride.MaximumMs + 1)]
    public void ValuesOutsideTheAllowedRange_AreRejected(int warning, int critical)
    {
        ResponseThresholdOverride.Decide(warning, critical).Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EqualThresholds_AreAllowed()
    {
        // Warning and critical at the same value collapses the warning band, which is a
        // deliberate "treat any breach as critical" choice rather than a mistake.
        ResponseThresholdOverride.Decide(2_000, 2_000).Error.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(1_500, 3_000, false)]
    [InlineData(1_000, 3_000, true)]
    [InlineData(1_500, 5_000, true)]
    public void IsOverride_ReportsOnlyValuesThatDifferFromTheDefaults(
        int? warning,
        int? critical,
        bool expected)
    {
        ResponseThresholdOverride.IsOverride(warning, critical).Should().Be(expected);
    }
}
