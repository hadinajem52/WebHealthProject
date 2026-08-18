using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using WebHealth.Infrastructure.Monitoring;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// The trust decision is tested directly against per-element status flags rather than through a
/// manufactured PKI, because the distinction that matters — which element a failure came from —
/// is exactly what the flags express.
/// </summary>
public sealed class TlsChainTrustTests
{
    [Fact]
    public void Evaluate_TrustsAChainThePlatformRaisedNoChainErrorsFor()
    {
        TlsChainTrust.Evaluate(SslPolicyErrors.None, []).Should().BeTrue();
        TlsChainTrust.Evaluate(SslPolicyErrors.RemoteCertificateNameMismatch, []).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ForgivesTimeValidityOnTheLeafSoExpiryKeepsItsOwnCategory()
    {
        var trusted = TlsChainTrust.Evaluate(
            SslPolicyErrors.RemoteCertificateChainErrors,
            [X509ChainStatusFlags.NotTimeValid, X509ChainStatusFlags.NoError]);

        trusted.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_RejectsAnExpiredIntermediateInsteadOfReportingAValidCertificate()
    {
        // Regression: the aggregate chain status cannot distinguish an expired leaf from an
        // expired issuer, so forgiving time validity chain-wide reported a genuinely broken
        // chain as Valid whenever the leaf's own dates happened to be fine.
        var trusted = TlsChainTrust.Evaluate(
            SslPolicyErrors.RemoteCertificateChainErrors,
            [X509ChainStatusFlags.NoError, X509ChainStatusFlags.NotTimeValid, X509ChainStatusFlags.NoError]);

        trusted.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_RejectsAnExpiredRoot()
    {
        var trusted = TlsChainTrust.Evaluate(
            SslPolicyErrors.RemoteCertificateChainErrors,
            [X509ChainStatusFlags.NoError, X509ChainStatusFlags.CtlNotTimeValid]);

        trusted.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_RejectsNonTimeFailuresOnTheLeaf()
    {
        var trusted = TlsChainTrust.Evaluate(
            SslPolicyErrors.RemoteCertificateChainErrors,
            [X509ChainStatusFlags.NotTimeValid | X509ChainStatusFlags.UntrustedRoot]);

        trusted.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_RejectsAnUntrustedRootOnASelfSignedChain()
    {
        TlsChainTrust.Evaluate(
                SslPolicyErrors.RemoteCertificateChainErrors,
                [X509ChainStatusFlags.UntrustedRoot])
            .Should().BeFalse();
    }

    [Fact]
    public void Evaluate_RejectsChainErrorsItCannotAttributeToAnyElement()
    {
        // A chain the platform refused but reported no elements for is not evidence of trust.
        TlsChainTrust.Evaluate(SslPolicyErrors.RemoteCertificateChainErrors, [])
            .Should().BeFalse();
    }

    [Fact]
    public void ReadElementStatuses_ReturnsNothingForAMissingChain() =>
        TlsChainTrust.ReadElementStatuses(null).Should().BeEmpty();
}
