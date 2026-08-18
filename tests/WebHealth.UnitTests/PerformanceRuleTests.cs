using FluentAssertions;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// BR-P01–BR-P05 on the pure evaluation surface: the threshold bands themselves, the findings
/// the normalizer builds from them, the page-size measurement and its label, and the
/// comparability assessment reports show alongside them.
/// </summary>
public sealed class PerformanceRuleTests
{
    private static readonly DateTimeOffset MeasuredAt = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1_499, PerformanceSeverity.None)]
    [InlineData(1_500, PerformanceSeverity.Warning)]
    [InlineData(2_999, PerformanceSeverity.Warning)]
    [InlineData(3_000, PerformanceSeverity.Critical)]
    [InlineData(30_000, PerformanceSeverity.Critical)]
    public void ResponseTimeSeverity_TreatsTheThresholdItselfAsABreach(
        int totalDurationMs,
        PerformanceSeverity expected)
    {
        // BR-P02: a 1,500 ms budget means "under 1,500 ms", so exactly 1,500 has missed it.
        PerformanceEvaluation.SelectResponseTimeSeverity(
            totalDurationMs, ResponseTimeThresholds.Default)
            .Should().Be(expected);
    }

    [Fact]
    public void ResponseTimeSeverity_HonoursEndpointOverrides()
    {
        var overrides = new ResponseTimeThresholds(500, 900);

        PerformanceEvaluation.SelectResponseTimeSeverity(600, overrides)
            .Should().Be(PerformanceSeverity.Warning);
        PerformanceEvaluation.SelectResponseTimeSeverity(600, ResponseTimeThresholds.Default)
            .Should().Be(PerformanceSeverity.None);
    }

    [Theory]
    [InlineData(2 * 1024 * 1024 - 1, PerformanceSeverity.None)]
    [InlineData(2 * 1024 * 1024, PerformanceSeverity.Warning)]
    public void PageSizeSeverity_WarnsAtOrAboveTheThreshold(long bytes, PerformanceSeverity expected)
    {
        PerformanceEvaluation.SelectPageSizeSeverity(
            bytes, PerformanceEvaluation.DefaultPageSizeWarningBytes)
            .Should().Be(expected);
    }

    [Fact]
    public void SlowResponse_IsRaisedAsItsOwnIssueSeparateFromAvailability()
    {
        // BR-I04: a slow 500 is two facts, tracked as two issues.
        var result = Normalize(Transport(TimeSpan.FromMilliseconds(4_000), status: 500));

        var slow = result.Findings.Should()
            .ContainSingle(finding => finding.RuleKey == PerformanceRules.SlowResponse).Subject;
        var serverError = result.Findings.Should()
            .ContainSingle(finding => finding.FailureCategory == HttpFailureCategories.ServerError)
            .Subject;
        slow.IssueKey.Should().NotBe(serverError.IssueKey);
        slow.Severity.Should().Be(FindingSeverities.Critical);
        slow.ObservedValue.Should().Be("4000 ms");
    }

    [Fact]
    public void SlowResponse_UsesTheThresholdsSnapshottedWithTheCheck()
    {
        // BR-P02: a historical result keeps the thresholds it was measured against, so the
        // same duration reads differently under a different snapshot.
        var strict = Normalize(
            Transport(TimeSpan.FromMilliseconds(800)),
            Policy(new ResponseTimeThresholds(500, 900)));
        var lenient = Normalize(Transport(TimeSpan.FromMilliseconds(800)));

        strict.Outcome.Should().Be(HttpResultOutcomes.Warning);
        strict.Findings.Should().ContainSingle().Which.Severity.Should().Be(FindingSeverities.Warning);
        lenient.Outcome.Should().Be(HttpResultOutcomes.Healthy);
    }

    [Fact]
    public void SlowResponse_NeedsThreeConsecutiveBreachesBeforeItCanConfirm()
    {
        // BR-P03 at the point the confirmation count is chosen.
        PerformanceRules.SelectFailureConfirmationCount(PerformanceRules.SlowResponse, 1)
            .Should().Be(3);
        PerformanceRules.SelectFailureConfirmationCount(PerformanceRules.SlowResponse, 5)
            .Should().Be(5);
        PerformanceRules.SelectFailureConfirmationCount("Http.ServerError", 1).Should().Be(1);
    }

    [Fact]
    public void PageSize_PrefersTheTransferredLengthAndLabelsIt()
    {
        // BR-P04: the advertised wire length is what a visitor downloads, and the label says so.
        // The decoded body is over the threshold and the transferred body is under it. Both
        // are stored; the rule judges the transferred one, and the label records that.
        var result = Normalize(Transport(
            TimeSpan.FromMilliseconds(100),
            bytesRead: 3_000_000,
            transferredLength: 900_000));

        result.TransferredLength.Should().Be(900_000);
        result.DecodedLength.Should().Be(3_000_000);
        result.LengthSource.Should().Be(PageLengthSources.TransferredContentLength);
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void PageSize_FallsBackToTheDecodedCountWhenNoLengthWasAdvertised()
    {
        var result = Normalize(Transport(TimeSpan.FromMilliseconds(100), bytesRead: 3_000_000));

        result.TransferredLength.Should().BeNull();
        result.DecodedLength.Should().Be(3_000_000);
        result.LengthSource.Should().Be(PageLengthSources.MeasuredDecoded);
        var finding = result.Findings.Should()
            .ContainSingle(candidate => candidate.RuleKey == PerformanceRules.PageTooLarge).Subject;
        finding.Severity.Should().Be(FindingSeverities.Warning);
        finding.ObservedValue.Should().Contain(PageLengthSources.MeasuredDecoded);
    }

    [Fact]
    public void PageSize_IsNotReportedTwiceForATruncatedBody()
    {
        // A truncated body already raises ResponseTooLarge, and its byte count is a lower
        // bound, so it is labelled as bounded and raises no page-size finding of its own.
        var cap = SafeHttpTransportDefaults.MaxDecodedBodyBytes;
        var result = Normalize(new SafeHttpTransportRequest(Guid.NewGuid(), "https://example.test/", true),
            new SafeHttpTransportResult(
                null, 200, new SafeHttpDestination("https://example.test/"),
                TimeSpan.FromMilliseconds(100), cap, true, new byte[cap], [],
                Timing: null));

        result.LengthSource.Should().Be(PageLengthSources.BoundedDecoded);
        result.Findings.Should().NotContain(finding => finding.RuleKey == PerformanceRules.PageTooLarge);
        result.Findings.Should().Contain(finding =>
            finding.FailureCategory == HttpFailureCategories.ResponseTooLarge);
    }

    [Fact]
    public void PageSize_IsNotMeasuredForAFailedExchange()
    {
        // BR-P01 missing-value handling: a failed exchange produced no page, which is not the
        // same as a page of zero bytes.
        var result = Normalize(new SafeHttpTransportRequest(Guid.NewGuid(), "https://example.test/", true),
            new SafeHttpTransportResult(
                SafeHttpFailureKind.Timeout, null, null, TimeSpan.FromSeconds(15), 0, false,
                ReadOnlyMemory<byte>.Empty, []));

        result.TransferredLength.Should().BeNull();
        result.DecodedLength.Should().BeNull();
        result.LengthSource.Should().BeNull();
        result.Findings.Should().NotContain(finding =>
            finding.RuleKey == PerformanceRules.SlowResponse
            || finding.RuleKey == PerformanceRules.PageTooLarge);
    }

    [Fact]
    public void Comparability_AcceptsResultsFromOneMonitorUnderOneConfiguration()
    {
        var assessment = PerformanceComparability.Evaluate([
            new PerformanceSampleContext(HttpResultNormalizer.MonitorSource, "fingerprint-1"),
            new PerformanceSampleContext(HttpResultNormalizer.MonitorSource, "fingerprint-1")
        ]);

        assessment.IsComparable.Should().BeTrue();
        assessment.Warning.Should().BeNull();
        assessment.MonitorSources.Should().ContainSingle();
    }

    [Fact]
    public void Comparability_WarnsWhenTheMonitorSourceDiffers()
    {
        // BR-P05: a certificate probe's duration and an HTTP check's duration are not the same
        // quantity, so the report says so rather than blending them.
        var assessment = PerformanceComparability.Evaluate([
            new PerformanceSampleContext(HttpResultNormalizer.MonitorSource, "fingerprint-1"),
            new PerformanceSampleContext(SslResultNormalizer.MonitorSource, "fingerprint-1")
        ]);

        assessment.IsComparable.Should().BeFalse();
        assessment.ConfigurationChanged.Should().BeFalse();
        assessment.Warning.Should().Contain("more than one monitor");
    }

    [Fact]
    public void Comparability_WarnsWhenTheCheckConfigurationChanged()
    {
        var assessment = PerformanceComparability.Evaluate([
            new PerformanceSampleContext(HttpResultNormalizer.MonitorSource, "fingerprint-1"),
            new PerformanceSampleContext(HttpResultNormalizer.MonitorSource, "fingerprint-2")
        ]);

        assessment.IsComparable.Should().BeFalse();
        assessment.ConfigurationChanged.Should().BeTrue();
        assessment.Warning.Should().Contain("configuration changed");
    }

    [Fact]
    public void Comparability_TreatsAnEmptyReportAsComparable()
    {
        PerformanceComparability.Evaluate([]).IsComparable.Should().BeTrue();
    }

    private static HttpResultPolicy Policy(ResponseTimeThresholds thresholds) =>
        HttpResultPolicy.Default with { ResponseTime = thresholds };

    private static NormalizedCheckResult Normalize(
        SafeHttpTransportResult transport,
        HttpResultPolicy? policy = null) =>
        Normalize(
            new SafeHttpTransportRequest(Guid.NewGuid(), "https://example.test/", true),
            transport,
            policy);

    private static NormalizedCheckResult Normalize(
        SafeHttpTransportRequest request,
        SafeHttpTransportResult transport,
        HttpResultPolicy? policy = null) =>
        HttpResultNormalizer.Normalize(new(
            request, transport, policy ?? HttpResultPolicy.Default, MeasuredAt));

    private static SafeHttpTransportResult Transport(
        TimeSpan duration,
        int status = 200,
        long bytesRead = 1_000,
        long? transferredLength = null) => new(
        null,
        status,
        new SafeHttpDestination("https://example.test/"),
        duration,
        bytesRead,
        false,
        new byte[Math.Min(bytesRead, 16)],
        [],
        TransferredLength: transferredLength);
}
