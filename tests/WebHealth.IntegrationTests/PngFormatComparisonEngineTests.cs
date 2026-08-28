using FluentAssertions;
using ImageMagick;
using WebHealth.Application.PngAudits;
using WebHealth.Infrastructure.PngAudits;
using Xunit;
using Xunit.Abstractions;

namespace WebHealth.IntegrationTests;

public sealed class PngFormatComparisonEngineTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("metadata-heavy.png", 32)]
    [InlineData("one-alpha-pixel.png", 42)]
    [InlineData("opaque-rgba.png", 32)]
    public async Task CompareAsync_StaysCloseToPinnedCwebp(
        string fixtureName,
        long cwebpBytes)
    {
        var source = LoadFixture(fixtureName);

        var result = await CreateEngine().CompareAsync(
            source,
            new(PngColorMeaning.AssumedSrgb),
            new PngRecommendationThresholds(0, 0),
            CancellationToken.None);

        result.Verified.Should().BeTrue(result.UnavailableReason);
        result.VerifiedWebpBytes.Should().BeLessThanOrEqualTo(cwebpBytes + 16);
        output.WriteLine(
            $"{fixtureName}: Magick.NET {result.VerifiedWebpBytes} bytes, cwebp 1.6.0 {cwebpBytes} bytes");
    }

    [Fact]
    public async Task CompareAsync_VerifiesHiddenRgbUnderFullyTransparentPixels()
    {
        var source = PngFixtureFactory.CreateHiddenTransparentRgbPng();

        var result = await CreateEngine().CompareAsync(
            source,
            new(PngColorMeaning.AssumedSrgb),
            new PngRecommendationThresholds(0, 0),
            CancellationToken.None);

        result.Verified.Should().BeTrue(result.UnavailableReason);
        result.VerifiedWebpBytes.Should().BePositive();
    }

    [Fact]
    public async Task CompareAsync_PreservesTheDeclaredIccProfile()
    {
        var source = PngFixtureFactory.CreateIccProfilePng();
        var encoded = await MagickPngFormatComparisonEngine.EncodeAsync(
            source,
            new PngAuditOptions().MaxImageBytes,
            CancellationToken.None);
        WebpContainer.TryInspect(encoded.Span, out _, out var candidateProfile).Should().BeTrue();
        using var sourceImage = new MagickImage(source);
        var sourceProfile = (sourceImage.GetProfile("icc") ?? sourceImage.GetProfile("icm"))
            ?.ToByteArray();
        sourceProfile.Should().NotBeNull();
        candidateProfile.Should().Equal(sourceProfile!);
        output.WriteLine(
            $"Source ICC: {sourceProfile?.Length}, candidate ICC: {candidateProfile?.Length}");

        var result = await CreateEngine().CompareAsync(
            source,
            new(PngColorMeaning.IccProfile),
            new PngRecommendationThresholds(0, 0),
            CancellationToken.None);

        result.Verified.Should().BeTrue(result.UnavailableReason);
        result.VerifiedWebpBytes.Should().BePositive();
    }

    [Fact]
    public async Task CompareAsync_StopsWritingWhenTheOutputLimitIsExceeded()
    {
        var options = new PngAuditOptions { MaxImageBytes = 32 };
        var source = LoadFixture("one-alpha-pixel.png");

        var result = await new MagickPngFormatComparisonEngine(options).CompareAsync(
            source,
            new(PngColorMeaning.AssumedSrgb),
            new PngRecommendationThresholds(0, 0),
            CancellationToken.None);

        result.Verified.Should().BeFalse();
        result.UnavailableReason.Should().Be("OutputLimitExceeded");
    }

    private static MagickPngFormatComparisonEngine CreateEngine() =>
        new(new PngAuditOptions());

    private static byte[] LoadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PngAudits", name));
}
