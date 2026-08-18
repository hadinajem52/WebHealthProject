using FluentAssertions;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class SslResultNormalizerTests
{
    private static readonly DateTimeOffset MeasuredAt = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normalize_ReportsAValidCertificateAsHealthyWithNoFindings()
    {
        var result = Normalize(Observed(TlsValidationCategory.Valid));

        result.Outcome.Should().Be(HttpResultOutcomes.Healthy);
        result.FailureCategory.Should().BeNull();
        result.Findings.Should().BeEmpty();
        result.SafeDiagnostic.Should().BeNull();
        result.MonitorSource.Should().Be(SslResultNormalizer.MonitorSource);
    }

    [Theory]
    [InlineData(TlsValidationCategory.Expired, SslFailureCategories.Expired)]
    [InlineData(TlsValidationCategory.NotYetValid, SslFailureCategories.NotYetValid)]
    [InlineData(TlsValidationCategory.HostnameMismatch, SslFailureCategories.HostnameMismatch)]
    [InlineData(TlsValidationCategory.Untrusted, SslFailureCategories.Untrusted)]
    public void Normalize_ReportsEveryInvalidCertificateAsCriticalWithItsOwnCategory(
        TlsValidationCategory category,
        string expected)
    {
        // BR-C03: each validation failure is critical and identifies its own cause.
        var result = Normalize(Observed(category));

        result.Outcome.Should().Be(HttpResultOutcomes.Critical);
        result.FailureCategory.Should().Be(expected);
        result.SafeDiagnostic.Should().NotBeNullOrWhiteSpace();
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.Severity.Should().Be(FindingSeverities.Critical);
        finding.IssueKey.Should().Be(SslMonitorIdentity.CreateIssueKey(expected));
    }

    [Fact]
    public void Normalize_ReportsAHandshakeFailureAsCriticalWithoutCertificateEvidence()
    {
        var result = Normalize(new(SslProbeFailureKind.HandshakeFailed, null, TimeSpan.FromSeconds(1)));

        result.Outcome.Should().Be(HttpResultOutcomes.Critical);
        result.FailureCategory.Should().Be(SslFailureCategories.HandshakeFailed);
        result.Findings.Should().ContainSingle().Which.ObservedValue
            .Should().Be("No certificate was presented");
    }

    [Theory]
    [InlineData(SslProbeFailureKind.NameResolution, HttpFailureCategories.Dns)]
    [InlineData(SslProbeFailureKind.Connection, HttpFailureCategories.Connection)]
    [InlineData(SslProbeFailureKind.Timeout, HttpFailureCategories.Timeout)]
    [InlineData(SslProbeFailureKind.DestinationRejected, HttpFailureCategories.DestinationPolicy)]
    [InlineData(SslProbeFailureKind.TargetNotAuthorized, HttpFailureCategories.DestinationPolicy)]
    [InlineData(SslProbeFailureKind.NotHttps, HttpFailureCategories.InvalidConfiguration)]
    public void Normalize_ReusesSharedCategoriesForTransportLevelFailures(
        SslProbeFailureKind failure,
        string expected)
    {
        var result = Normalize(new(failure, null, TimeSpan.FromSeconds(1)));

        result.Outcome.Should().Be(HttpResultOutcomes.Critical);
        result.FailureCategory.Should().Be(expected);
    }

    [Fact]
    public void Normalize_ReportsCallerCancellationWithoutRaisingAFinding()
    {
        // A cancelled probe observed nothing; it must not open an incident.
        var result = Normalize(new(SslProbeFailureKind.Cancelled, null, TimeSpan.FromSeconds(1)));

        result.Outcome.Should().Be(HttpResultOutcomes.Cancelled);
        result.FailureCategory.Should().Be(HttpFailureCategories.Cancellation);
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_RecordsNoRedirectsOrBodyEvidence()
    {
        var result = Normalize(Observed(TlsValidationCategory.Valid));

        result.Redirects.Should().BeEmpty();
        result.HttpStatus.Should().BeNull();
        result.DecodedLength.Should().BeNull();
        result.LengthSource.Should().BeNull();
    }

    private static NormalizedCheckResult Normalize(SslCertificateProbeResult probe) =>
        SslResultNormalizer.Normalize(new(probe, MeasuredAt));

    private static SslCertificateProbeResult Observed(TlsValidationCategory category) => new(
        null,
        new TlsCertificateObservation(
            "CN=example.test",
            "CN=Example CA",
            "01",
            new string('a', 64),
            MeasuredAt.AddDays(-30),
            MeasuredAt.AddDays(30),
            ["example.test"],
            category != TlsValidationCategory.HostnameMismatch,
            category is TlsValidationCategory.Valid or TlsValidationCategory.Expired,
            category,
            MeasuredAt),
        TimeSpan.FromMilliseconds(120));
}

public sealed class CertificateExpiryTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(30, 30)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void DaysRemaining_CountsWholeDaysLeft(int daysAhead, int expected)
    {
        CertificateExpiry.DaysRemaining(ObservedAt.AddDays(daysAhead), ObservedAt)
            .Should().Be(expected);
    }

    [Fact]
    public void DaysRemaining_TruncatesRatherThanRounding()
    {
        // 29 hours left is one whole day, not two.
        CertificateExpiry.DaysRemaining(ObservedAt.AddHours(29), ObservedAt).Should().Be(1);
    }

    [Fact]
    public void DaysRemaining_ReportsExpiredCertificatesAsNegative()
    {
        // Clamping to zero would make "expires today" and "expired last week" identical.
        CertificateExpiry.DaysRemaining(ObservedAt.AddDays(-7), ObservedAt).Should().Be(-7);
    }
}
