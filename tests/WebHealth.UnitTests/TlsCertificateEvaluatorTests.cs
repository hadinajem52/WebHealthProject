using FluentAssertions;
using WebHealth.Domain.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class TlsCertificateEvaluatorTests
{
    private static readonly DateTimeOffset NotBefore = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NotAfter = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Classify_ReportsValidInsideTheWindowWhenTrustedAndMatched()
    {
        Classify(NotBefore.AddDays(30), hostnameMatched: true, chainTrusted: true)
            .Should().Be(TlsValidationCategory.Valid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Classify_TreatsBothEndsOfTheValidityWindowAsInclusive(int endOffsetTicks)
    {
        // RFC 5280 includes both bounds: a certificate is valid at exactly notBefore and at
        // exactly notAfter, and only becomes invalid one tick outside the window.
        var atStart = endOffsetTicks == 0 ? NotBefore : NotBefore.AddTicks(-1);
        var atEnd = endOffsetTicks == 0 ? NotAfter : NotAfter.AddTicks(1);

        Classify(atStart, hostnameMatched: true, chainTrusted: true)
            .Should().Be(endOffsetTicks == 0 ? TlsValidationCategory.Valid : TlsValidationCategory.NotYetValid);
        Classify(atEnd, hostnameMatched: true, chainTrusted: true)
            .Should().Be(endOffsetTicks == 0 ? TlsValidationCategory.Valid : TlsValidationCategory.Expired);
    }

    [Fact]
    public void Classify_ReportsExpiryAheadOfTrustAndHostnameProblems()
    {
        // An expired certificate almost always also reports chain errors. Reporting it as
        // untrusted would hide the cause the operator has to act on.
        Classify(NotAfter.AddDays(1), hostnameMatched: false, chainTrusted: false)
            .Should().Be(TlsValidationCategory.Expired);
    }

    [Fact]
    public void Classify_ReportsNotYetValidAheadOfTrustAndHostnameProblems()
    {
        Classify(NotBefore.AddDays(-1), hostnameMatched: false, chainTrusted: false)
            .Should().Be(TlsValidationCategory.NotYetValid);
    }

    [Fact]
    public void Classify_ReportsHostnameMismatchAheadOfTrust()
    {
        Classify(NotBefore.AddDays(30), hostnameMatched: false, chainTrusted: false)
            .Should().Be(TlsValidationCategory.HostnameMismatch);
    }

    [Fact]
    public void Classify_ReportsUntrustedWhenOnlyTheChainFails()
    {
        Classify(NotBefore.AddDays(30), hostnameMatched: true, chainTrusted: false)
            .Should().Be(TlsValidationCategory.Untrusted);
    }

    private static TlsValidationCategory Classify(
        DateTimeOffset evaluatedAt, bool hostnameMatched, bool chainTrusted) =>
        TlsCertificateEvaluator.Classify(NotBefore, NotAfter, evaluatedAt, hostnameMatched, chainTrusted);
}
