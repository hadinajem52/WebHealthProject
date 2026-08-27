using System.Buffers.Binary;
using WebHealth.Application.PngAudits;

namespace WebHealth.Infrastructure.PngAudits;

internal readonly record struct PngChunkPreflight(
    int Width,
    int Height,
    int FrameCount,
    byte BitDepth,
    byte ColorType)
{
    public long PixelCount => (long)Width * Height;

    public bool HasSupportedBitDepth => BitDepth <= 8;

    public PngImageFacts CreateFacts(long? transparentPixelCount = null) =>
        new(Width, Height, FrameCount, PixelCount, transparentPixelCount);
}

internal static class PngChunkInspector
{
    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const int SignatureLength = 8;
    private const int IhdrDataLength = 13;
    private const int ActlDataLength = 8;
    private const int MaxChunksBeforeImageData = 4096;

    public static bool TryInspect(ReadOnlySpan<byte> encoded, out PngChunkPreflight preflight)
    {
        preflight = default;
        if (!HasPngSignature(encoded)
            || !TryReadHeader(encoded, out var header, out var nextOffset))
        {
            return false;
        }

        return TryFindImageData(encoded, header, nextOffset, out preflight);
    }

    private static bool HasPngSignature(ReadOnlySpan<byte> encoded) =>
        encoded.Length >= SignatureLength
        && encoded[..SignatureLength].SequenceEqual(PngSignature);

    private static bool TryReadHeader(
        ReadOnlySpan<byte> encoded,
        out PngChunkPreflight header,
        out int nextOffset)
    {
        header = default;
        nextOffset = SignatureLength;
        if (!PngChunkReader.TryRead(encoded, nextOffset, out var chunk)
            || !chunk.HasType("IHDR"u8)
            || chunk.Length != IhdrDataLength
            || !chunk.HasValidChecksum
            || !TryReadDimensions(chunk.Data, out var width, out var height)
            || !HasValidHeaderSettings(chunk.Data))
        {
            return false;
        }

        header = new(width, height, 1, chunk.Data[8], chunk.Data[9]);
        nextOffset = chunk.NextOffset;
        return true;
    }

    private static bool TryReadDimensions(
        ReadOnlySpan<byte> data,
        out int width,
        out int height)
    {
        var encodedWidth = BinaryPrimitives.ReadUInt32BigEndian(data);
        var encodedHeight = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        if (encodedWidth is 0 or > int.MaxValue || encodedHeight is 0 or > int.MaxValue)
        {
            width = 0;
            height = 0;
            return false;
        }

        width = (int)encodedWidth;
        height = (int)encodedHeight;
        return true;
    }

    private static bool HasValidHeaderSettings(ReadOnlySpan<byte> data) =>
        HasValidBitDepth(data[8], data[9])
        && data[10] == 0
        && data[11] == 0
        && data[12] <= 1;

    private static bool HasValidBitDepth(byte bitDepth, byte colorType) =>
        colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 => bitDepth is 8 or 16,
            6 => bitDepth is 8 or 16,
            _ => false
        };

    private static bool TryFindImageData(
        ReadOnlySpan<byte> encoded,
        PngChunkPreflight header,
        int offset,
        out PngChunkPreflight preflight)
    {
        var declaredFrameCount = 0;
        for (var chunkCount = 0; chunkCount < MaxChunksBeforeImageData; chunkCount++)
        {
            if (!PngChunkReader.TryRead(encoded, offset, out var chunk))
            {
                break;
            }

            if (chunk.HasType("acTL"u8)
                && !TryReadAnimationControl(chunk, ref declaredFrameCount))
            {
                break;
            }

            if (chunk.HasType("IDAT"u8))
            {
                return TryCreatePreflight(encoded, header, declaredFrameCount, out preflight);
            }

            if (chunk.HasType("IEND"u8))
            {
                break;
            }

            offset = chunk.NextOffset;
        }

        preflight = default;
        return false;
    }

    private static bool TryReadAnimationControl(PngChunk chunk, ref int declaredFrameCount)
    {
        if (declaredFrameCount != 0
            || chunk.Length != ActlDataLength
            || !chunk.HasValidChecksum)
        {
            return false;
        }

        var encodedFrameCount = BinaryPrimitives.ReadUInt32BigEndian(chunk.Data);
        if (encodedFrameCount is 0 or > int.MaxValue)
        {
            return false;
        }

        declaredFrameCount = (int)encodedFrameCount;
        return true;
    }

    private static bool TryCreatePreflight(
        ReadOnlySpan<byte> encoded,
        PngChunkPreflight header,
        int declaredFrameCount,
        out PngChunkPreflight preflight)
    {
        if (declaredFrameCount == 0)
        {
            preflight = header;
            return true;
        }

        preflight = header with { FrameCount = declaredFrameCount };
        return declaredFrameCount > 1
            && PngAnimationValidator.TryValidate(encoded, preflight);
    }
}
