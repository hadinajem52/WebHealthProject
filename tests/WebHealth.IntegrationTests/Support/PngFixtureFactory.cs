using ImageMagick;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace WebHealth.IntegrationTests;

internal static class PngFixtureFactory
{
    public static byte[] CreateIccProfilePng()
    {
        using var image = new MagickImage(new MagickColor("#285078"), 8, 8);
        image.SetProfile(ColorProfiles.AdobeRGB1998);
        return image.ToByteArray(MagickFormat.Png);
    }

    public static byte[] CreateHiddenTransparentRgbPng()
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(40, 80, 120, byte.MaxValue));
        image[0, 0] = new Rgba32(11, 22, 33, 0);
        using var output = new MemoryStream();
        image.SaveAsPng(output, new PngEncoder
        {
            BitDepth = PngBitDepth.Bit8,
            ColorType = PngColorType.RgbWithAlpha,
            CompressionLevel = PngCompressionLevel.BestCompression,
            SkipMetadata = true
        });
        return output.ToArray();
    }
}
