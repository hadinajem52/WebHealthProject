using FluentAssertions;
using WebHealth.Application.PngAudits;
using WebHealth.Domain.Crawling;
using WebHealth.Infrastructure.PngAudits;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PngAssetDiscoveryTests
{
    private static readonly Uri BaseUrl = new("https://site.test/app/page");

    [Fact]
    public void Resolve_PreservesQueryOrderAndEncodingWhileRemovingTheFragment()
    {
        var result = PngAssetUrlResolver.Resolve(
            "/assets/image.png?b=2&token=secret&a=%2f#preview",
            BaseUrl,
            CrawlUrlOptions.Default);

        result.Asset.Should().NotBeNull();
        result.Asset!.FetchUrl.Should().Be(
            "https://site.test/assets/image.png?b=2&token=secret&a=%2f");
        result.Asset.DisplayUrl.Should().Be(
            "https://site.test/assets/image.png?b=2&token=REDACTED&a=%2f");
    }

    [Fact]
    public void Resolve_TreatsQueryOrderAsPartOfTheRequestIdentity()
    {
        var first = Resolve("/image.png?w=400&h=200");
        var second = Resolve("/image.png?h=200&w=400");

        first.IdentityHash.Should().NotBe(second.IdentityHash);
        first.FetchUrl.Should().EndWith("?w=400&h=200");
        second.FetchUrl.Should().EndWith("?h=200&w=400");
    }

    [Fact]
    public void Resolve_CanonicalizesEquivalentTransportRequestsForIdentityOnly()
    {
        var first = Resolve("HTTPS://SITE.TEST:443/image.png?sig=%2f");
        var second = Resolve("https://site.test/image.png?sig=%2F");

        first.FetchUrl.Should().NotBe(second.FetchUrl);
        first.IdentityHash.Should().Be(second.IdentityHash);
    }

    [Fact]
    public void ResolutionBase_ResolvesRelativeAssetsFromTheDocumentBaseElement()
    {
        var resolutionBase = PngAssetUrlResolver.ResolutionBase(
            BaseUrl.AbsoluteUri,
            "/assets/gallery/");

        var result = PngAssetUrlResolver.Resolve(
            "../image.png",
            resolutionBase,
            CrawlUrlOptions.Default);

        result.Asset!.FetchUrl.Should().Be("https://site.test/assets/image.png");
    }

    [Theory]
    [InlineData("data:image/png;base64,AAAA", PngDiscoverySkipReason.DataUrl)]
    [InlineData("blob:https://site.test/id", PngDiscoverySkipReason.BlobUrl)]
    [InlineData("ftp://site.test/image.png", PngDiscoverySkipReason.UnsupportedScheme)]
    [InlineData("https://user:password@site.test/image.png", PngDiscoverySkipReason.CredentialsPresent)]
    [InlineData("http://[invalid", PngDiscoverySkipReason.MalformedUrl)]
    public void Resolve_ClassifiesUnsafeOrUnsupportedValues(
        string rawUrl,
        PngDiscoverySkipReason expected)
    {
        var result = PngAssetUrlResolver.Resolve(rawUrl, BaseUrl, CrawlUrlOptions.Default);

        result.Succeeded.Should().BeFalse();
        result.Rejection.Should().Be(expected);
    }

    [Fact]
    public void SafeRawValue_RedactsAndBoundsRejectedReferences()
    {
        var raw = $"/image.png?token=secret&value={new string('a', 600)}";

        var safe = PngAssetUrlResolver.SafeRawValue(raw, CrawlUrlOptions.Default);

        safe.Should().HaveLength(PngAssetUrlResolver.MaxSafeRawValueLength);
        safe.Should().Contain("token=REDACTED");
        safe.Should().NotContain("token=secret");
    }

    private static PngResolvedAsset Resolve(string rawUrl) =>
        PngAssetUrlResolver.Resolve(rawUrl, BaseUrl, CrawlUrlOptions.Default).Asset!;
}
