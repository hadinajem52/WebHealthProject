using System.Net;
using FluentAssertions;
using WebHealth.Domain.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class DestinationAddressPolicyTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:0:192.0.2.1")]
    [InlineData("64:ff9b::1")]
    [InlineData("64:ff9b:1::1")]
    [InlineData("100::1")]
    [InlineData("2001::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002::1")]
    [InlineData("3fff::1")]
    [InlineData("5f00::1")]
    [InlineData("2620:4f:8000::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff00::1")]
    public void IsAllowed_RejectsSpecialPurposeAddresses(string value)
    {
        DestinationAddressPolicy.IsAllowed(IPAddress.Parse(value)).Should().BeFalse();
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2606:4700:4700::1111")]
    public void IsAllowed_AcceptsPublicAddresses(string value)
    {
        DestinationAddressPolicy.IsAllowed(IPAddress.Parse(value)).Should().BeTrue();
    }
}
