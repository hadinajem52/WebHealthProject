using FluentAssertions;
using WebHealth.Domain.Normalization;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class EndpointUrlNormalizerTests
{
    [Theory]
    [InlineData("/relative")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("https://example.com/path#section")]
    public void Normalize_RejectsUnsupportedOrAmbiguousInput(string value)
    {
        EndpointUrlNormalizer.Normalize(value).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Normalize_CanonicalizesIdentityAndProducesStableHash()
    {
        var first = EndpointUrlNormalizer.Normalize(" HTTPS://BÜCHER.example.:443/a/../%7Euser?q=%41 ");
        var second = EndpointUrlNormalizer.Normalize("https://xn--bcher-kva.example/~user?q=A");

        first.Succeeded.Should().BeTrue();
        first.NormalizedUrl.Should().Be("https://xn--bcher-kva.example/~user?q=A");
        first.NormalizedUrl.Should().Be(second.NormalizedUrl);
        first.NormalizedUrlHash.Should().Equal(second.NormalizedUrlHash!);
        first.NormalizedUrlHash.Should().HaveCount(32);
        first.NormalizedHost.Should().Be("xn--bcher-kva.example");
        first.EffectivePort.Should().Be(443);
    }

    [Fact]
    public void Normalize_PreservesPathCaseQueryOrderAndTrailingSlashIdentity()
    {
        var result = EndpointUrlNormalizer.Normalize("http://Example.com:80/Path/?b=2&a=1");

        result.NormalizedUrl.Should().Be("http://example.com/Path/?b=2&a=1");
        result.NormalizedHost.Should().Be("example.com");
        result.EffectivePort.Should().Be(80);
    }
}
