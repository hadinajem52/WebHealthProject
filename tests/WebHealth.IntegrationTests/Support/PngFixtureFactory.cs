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

    public static byte[] CreatePaletteReferencePng(int size)
    {
        using var image = new Image<Rgba32>(size, size);
        var row = new Rgba32[size];
        uint state = 2166136261;
        for (var x = 0; x < size; x++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var index = (int)(state & 3);
            row[x] = index switch
            {
                0 => new Rgba32(12, 24, 48),
                1 => new Rgba32(220, 230, 240),
                2 => new Rgba32(80, 140, 200),
                _ => new Rgba32(240, 160, 40)
            };
        }
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                image[x, y] = row[x];
            }
        }
        using var output = new MemoryStream();
        image.SaveAsPng(output, new PngEncoder
        {
            ColorType = PngColorType.Palette,
            CompressionLevel = PngCompressionLevel.NoCompression,
            FilterMethod = PngFilterMethod.Adaptive,
            SkipMetadata = true
        });
        return output.ToArray();
    }

    public static byte[] CreateGrayscaleReferencePng(int size)
    {
        using var image = new Image<L8>(size, size);
        var row = new L8[size];
        uint state = 2166136261;
        for (var x = 0; x < size; x++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            row[x] = new L8((state & 1) == 0 ? byte.MinValue : byte.MaxValue);
        }
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                image[x, y] = row[x];
            }
        }
        using var output = new MemoryStream();
        image.SaveAsPng(output, new PngEncoder
        {
            BitDepth = PngBitDepth.Bit1,
            ColorType = PngColorType.Grayscale,
            CompressionLevel = PngCompressionLevel.NoCompression,
            FilterMethod = PngFilterMethod.Adaptive,
            SkipMetadata = true
        });
        return output.ToArray();
    }
}
