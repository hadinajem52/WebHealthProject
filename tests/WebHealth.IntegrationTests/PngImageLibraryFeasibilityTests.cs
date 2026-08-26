using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PngImageLibraryFeasibilityTests
{
    [Theory]
    [InlineData("opaque-rgba.png", 0)]
    [InlineData("one-alpha-pixel.png", 1)]
    public void ImageSharp_IdentifiesPngAndDetectsTransparencyFromDecodedPixels(
        string fixtureName,
        long expectedTransparentPixels)
    {
        var bytes = LoadFixture(fixtureName);

        Image.DetectFormat(bytes).Name.Should().Be("PNG");
        var information = Image.Identify(bytes);
        information.Width.Should().BePositive();
        information.Height.Should().BePositive();

        using var image = Image.Load<Rgba32>(bytes);

        CountTransparentPixels(image).Should().Be(expectedTransparentPixels);
    }

    [Fact]
    public void ImageSharp_DetectsApngFromTheDecodedFrameCount()
    {
        var bytes = LoadFixture("animated.png");

        using var image = Image.Load<Rgba32>(bytes);

        Image.DetectFormat(bytes).Name.Should().Be("PNG");
        image.Metadata.DecodedImageFormat?.Name.Should().Be("PNG");
        image.Frames.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task ImageSharp_StripsMetadataAndProducesRealNormalizedPngAndLosslessWebpSizes()
    {
        var bytes = LoadFixture("metadata-heavy.png");
        using var image = Image.Load<Rgba32>(bytes);
        image.Metadata.GetPngMetadata().TextData.Should().NotBeEmpty();

        await using var normalizedPng = new MemoryStream();
        await image.SaveAsPngAsync(normalizedPng, new PngEncoder { SkipMetadata = true });
        await using var losslessWebp = new MemoryStream();
        await image.SaveAsWebpAsync(losslessWebp, new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossless,
            Method = WebpEncodingMethod.BestQuality,
            Quality = 100,
            SkipMetadata = true
        });

        normalizedPng.Length.Should().BePositive();
        losslessWebp.Length.Should().BePositive();
        normalizedPng.Length.Should().BeLessThan(bytes.Length);

        normalizedPng.Position = 0;
        using var normalized = Image.Load<Rgba32>(normalizedPng);
        normalized.Metadata.GetPngMetadata().TextData.Should().BeEmpty();

        losslessWebp.Position = 0;
        using var webp = Image.Load<Rgba32>(losslessWebp);
        webp.Size.Should().Be(image.Size);
        webp[0, 0].Should().Be(image[0, 0]);
    }

    private static byte[] LoadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PngAudits", name));

    private static long CountTransparentPixels(Image<Rgba32> image)
    {
        long count = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                foreach (var pixel in accessor.GetRowSpan(y))
                {
                    if (pixel.A < byte.MaxValue)
                    {
                        count++;
                    }
                }
            }
        });
        return count;
    }
}
