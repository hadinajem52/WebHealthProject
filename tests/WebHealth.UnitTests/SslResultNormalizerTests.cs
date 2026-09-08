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
    [InlineData(TlsValidationCategory.NotYetValid, SslFailureCategories.NotYetValid)]
    [InlineData(TlsValidationCategory.HostnameMismatch, SslFailureCategories.HostnameMismatch)]
    [InlineData(TlsValidationCategory.Untrusted, SslFailureCategories.Untrusted)]
    public void Normalize_ReportsEveryInvalidCertificateAsCriticalWithItsOwnCategory(
        TlsValidationCategory category,
        string expected)
    {
        var result = Normalize(Observed(category));

        result.Outcome.Should().Be(HttpResultOutcomes.Critical);
        result.FailureCategory.Should().Be(expected);
        result.SafeDiagnostic.Should().NotBeNullOrWhiteSpace();
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.Severity.Should().Be(FindingSeverities.Critical);
        finding.IssueKey.Should().Be(SslMonitorIdentity.CreateIssueKey(expected));
    }

    [Fact]
    public void Normalize_ReportsAnExpiredCertificateOnTheSameIssueKeyItUsedWhileValid()
    {
        var expiringSoon = Normalize(Observed(TlsValidationCategory.Valid, 3));
        var expired = Normalize(Observed(TlsValidationCategory.Expired, -1));

        expired.Outcome.Should().Be(HttpResultOutcomes.Critical);
        expired.FailureCategory.Should().Be(SslFailureCategories.Expired);
        expired.SafeDiagnostic.Should().NotBeNullOrWhiteSpace();
        var finding = expired.Findings.Should().ContainSingle().Subject;
        finding.Severity.Should().Be(FindingSeverities.Critical);
        finding.IssueKey
            .Should().Be(SslMonitorIdentity.CreateExpiryIssueKey(Fingerprint('a')))
            .And.Be(expiringSoon.Findings.Single().IssueKey);
    }

    [Fact]
    public void IsSupersededExpiryIssueKey_RecognisesAnExpiredCertificatesIssueKey()
    {
        var issueKey = Normalize(Observed(TlsValidationCategory.Expired, -1)).Findings.Single().IssueKey;

        SslMonitorIdentity.IsSupersededExpiryIssueKey(issueKey, Fingerprint('b')).Should().BeTrue();
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

    [Theory]
    [InlineData(31, null)]
    [InlineData(30, FindingSeverities.Warning)]
    [InlineData(16, FindingSeverities.Warning)]
    [InlineData(15, FindingSeverities.High)]
    [InlineData(8, FindingSeverities.High)]
    [InlineData(7, FindingSeverities.Critical)]
    [InlineData(0, FindingSeverities.Critical)]
    public void Normalize_RaisesTheExpiryBandForAValidCertificate(int daysAhead, string? expected)
    {
        var result = Normalize(Observed(TlsValidationCategory.Valid, daysAhead));

        if (expected is null)
        {
            result.Outcome.Should().Be(HttpResultOutcomes.Healthy);
            result.FailureCategory.Should().BeNull();
            result.Findings.Should().BeEmpty();
            return;
        }

        result.FailureCategory.Should().Be(SslFailureCategories.ExpiringSoon);
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.Severity.Should().Be(expected);
        finding.RuleKey.Should().Be(SslMonitorIdentity.ExpiryRuleKey);
        result.SafeDiagnostic.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(30, HttpResultOutcomes.Warning)]
    [InlineData(15, HttpResultOutcomes.Warning)]
    [InlineData(7, HttpResultOutcomes.Critical)]
    public void Normalize_KeepsAnExpiringCertificateOutOfCriticalUntilTheCriticalBand(
        int daysAhead,
        string expectedOutcome)
    {
        Normalize(Observed(TlsValidationCategory.Valid, daysAhead)).Outcome
            .Should().Be(expectedOutcome);
    }

    [Fact]
    public void Normalize_KeysTheExpiryIssueByFingerprint()
    {
        var first = Normalize(Observed(TlsValidationCategory.Valid, 10, Fingerprint('a')));
        var repeat = Normalize(Observed(TlsValidationCategory.Valid, 9, Fingerprint('a')));
        var renewed = Normalize(Observed(TlsValidationCategory.Valid, 10, Fingerprint('b')));

        first.Findings.Single().IssueKey
            .Should().Be(SslMonitorIdentity.CreateExpiryIssueKey(Fingerprint('a')))
            .And.Be(repeat.Findings.Single().IssueKey);
        renewed.Findings.Single().IssueKey.Should().NotBe(first.Findings.Single().IssueKey);
    }

    [Theory]
    [InlineData(-1, SslFailureCategories.Expired)]
    [InlineData(10, SslFailureCategories.HostnameMismatch)]
    public void Normalize_PreservesSimultaneousExpiryHostnameAndTrustFindings(int daysUntilExpiry, string primary)
    {
        var probe = Observed(TlsValidationCategory.Valid, daysUntilExpiry);
        probe = probe with { Certificate = probe.Certificate! with { HostnameMatched = false, ChainTrusted = false } };

        var result = Normalize(probe);

        result.FailureCategory.Should().Be(primary);
        result.Outcome.Should().Be(HttpResultOutcomes.Critical);
        result.Findings.Select(finding => finding.RuleKey).Should().BeEquivalentTo(
            SslMonitorIdentity.ExpiryRuleKey, SslMonitorIdentity.HostnameMismatchRuleKey, SslMonitorIdentity.UntrustedRuleKey);
        result.Findings.Single(finding => finding.RuleKey == SslMonitorIdentity.HostnameMismatchRuleKey).IssueKey
            .Should().Be(SslMonitorIdentity.CreateIssueKey(SslFailureCategories.HostnameMismatch));
    }

    [Fact]
    public void Normalize_NotYetValidIsPrimaryWithoutSuppressingOtherFaults()
    {
        var probe = Observed(TlsValidationCategory.NotYetValid, 10);
        probe = probe with { Certificate = probe.Certificate! with { HostnameMatched = false, ChainTrusted = false } };

        var result = Normalize(probe);

        result.FailureCategory.Should().Be(SslFailureCategories.NotYetValid);
        result.Findings.Select(finding => finding.RuleKey).Should().BeEquivalentTo(
            SslMonitorIdentity.NotYetValidRuleKey, SslMonitorIdentity.ExpiryRuleKey,
            SslMonitorIdentity.HostnameMismatchRuleKey, SslMonitorIdentity.UntrustedRuleKey);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsSupersededExpiryIssueKey_RecognisesOnlyExpiryKeysForOtherCertificates(bool renewed)
    {
        var issueKey = SslMonitorIdentity.CreateExpiryIssueKey(Fingerprint('a'));
        var observedFingerprint = renewed ? Fingerprint('b') : Fingerprint('a');

        SslMonitorIdentity.IsSupersededExpiryIssueKey(issueKey, observedFingerprint)
            .Should().Be(renewed);
        SslMonitorIdentity.IsSupersededExpiryIssueKey(
            SslMonitorIdentity.CreateIssueKey(SslFailureCategories.HostnameMismatch),
            observedFingerprint)
            .Should().BeFalse();
    }

    private static NormalizedCheckResult Normalize(SslCertificateProbeResult probe) =>
        SslResultNormalizer.Normalize(new(probe, MeasuredAt));

    private static string Fingerprint(char seed) => new(seed, 64);

    private static SslCertificateProbeResult Observed(
        TlsValidationCategory category,
        int daysUntilExpiry = 90,
        string? fingerprint = null) => new(
        null,
        new TlsCertificateObservation(
            "CN=example.test",
            "CN=Example CA",
            "01",
            fingerprint ?? Fingerprint('a'),
            category == TlsValidationCategory.NotYetValid ? MeasuredAt.AddDays(1) : MeasuredAt.AddDays(-30),
            MeasuredAt.AddDays(daysUntilExpiry),
            ["example.test"],
            category != TlsValidationCategory.HostnameMismatch,
            category != TlsValidationCategory.Untrusted,
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
        CertificateExpiry.DaysRemaining(ObservedAt.AddHours(29), ObservedAt).Should().Be(1);
    }

    [Fact]
    public void DaysRemaining_ReportsExpiredCertificatesAsNegative()
    {
        CertificateExpiry.DaysRemaining(ObservedAt.AddDays(-7), ObservedAt).Should().Be(-7);
    }

    [Theory]
    [InlineData(int.MaxValue, CertificateExpirySeverity.None)]
    [InlineData(31, CertificateExpirySeverity.None)]
    [InlineData(30, CertificateExpirySeverity.Warning)]
    [InlineData(16, CertificateExpirySeverity.Warning)]
    [InlineData(15, CertificateExpirySeverity.High)]
    [InlineData(8, CertificateExpirySeverity.High)]
    [InlineData(7, CertificateExpirySeverity.Critical)]
    [InlineData(1, CertificateExpirySeverity.Critical)]
    [InlineData(0, CertificateExpirySeverity.Critical)]
    public void SelectSeverity_TreatsEveryBoundaryDayAsInsideItsBand(
        int daysRemaining,
        CertificateExpirySeverity expected)
    {
        CertificateExpiry.SelectSeverity(daysRemaining, CertificateExpiryThresholds.Default)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-400)]
    public void SelectSeverity_TreatsAnAlreadyExpiredCertificateAsCritical(int daysRemaining)
    {
        CertificateExpiry.SelectSeverity(daysRemaining, CertificateExpiryThresholds.Default)
            .Should().Be(CertificateExpirySeverity.Critical);
    }

    [Fact]
    public void SelectSeverity_HonoursOverriddenThresholds()
    {
        var thresholds = new CertificateExpiryThresholds(60, 40, 20);

        CertificateExpiry.SelectSeverity(60, thresholds).Should().Be(CertificateExpirySeverity.Warning);
        CertificateExpiry.SelectSeverity(61, thresholds).Should().Be(CertificateExpirySeverity.None);
        CertificateExpiry.SelectSeverity(20, thresholds).Should().Be(CertificateExpirySeverity.Critical);
    }

    [Theory]
    [InlineData(30, 40, 7)]
    [InlineData(30, 15, -1)]
    [InlineData(30, 30, 7)]
    [InlineData(30, 7, 7)]
    public void SelectSeverity_RejectsUnorderedThresholds(int warning, int high, int critical)
    {
        var act = () => CertificateExpiry.SelectSeverity(
            10, new CertificateExpiryThresholds(warning, high, critical));

        act.Should().Throw<ArgumentException>();
    }
}
