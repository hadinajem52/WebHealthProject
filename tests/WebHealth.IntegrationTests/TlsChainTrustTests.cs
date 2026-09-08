using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using WebHealth.Infrastructure.Monitoring;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class TlsChainTrustTests
{
    [Fact]
    public void CanonicalStatusCodes_ExpandsFlagsDeduplicatesAndSortsWithoutNoError()
    {
        TlsChainTrust.CanonicalStatusCodes([
            X509ChainStatusFlags.NoError,
            X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.NotTimeValid,
            X509ChainStatusFlags.PartialChain,
            X509ChainStatusFlags.UntrustedRoot])
            .Should().Equal("NotTimeValid", "PartialChain", "UntrustedRoot");
        TlsChainTrust.CanonicalStatusCodes([X509ChainStatusFlags.NoError]).Should().BeEmpty();
    }

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
        TlsChainTrust.Evaluate(SslPolicyErrors.RemoteCertificateChainErrors, [])
            .Should().BeFalse();
    }

    [Fact]
    public void ReadElementStatuses_ReturnsNothingForAMissingChain() =>
        TlsChainTrust.ReadElementStatuses(null).Should().BeEmpty();
}
