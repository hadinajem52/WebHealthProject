using FluentAssertions;
using WebHealth.Application.Reporting;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class CertificateExpirySummaryTests
{
    [Fact]
    public void CertificatesWithBothValidityAndExpiryFaultsCountOnceForAttention()
    {
        var summary = new ReportCertificateExpiry(0, 0, 0, 3, 1, 1, 2, [], 2);

        summary.AttentionCount.Should().Be(5);
        summary.InvalidCount.Should().Be(3);
        summary.CriticalCount.Should().Be(2);
    }
}
