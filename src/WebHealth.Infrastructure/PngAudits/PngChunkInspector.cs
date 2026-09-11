using System.Buffers.Binary;
using WebHealth.Application.PngAudits;

namespace WebHealth.Infrastructure.PngAudits;

internal readonly record struct PngChunkPreflight(
    int Width,
    int Height,
    int FrameCount,
    byte BitDepth,
    byte ColorType,
    PngColorMeaning ColorMeaning = PngColorMeaning.AssumedSrgb,
    bool HasAnimation = false,
    PngPresentationOrientation Orientation = PngPresentationOrientation.Identity)
{
    public long PixelCount => (long)Width * Height;

    public bool IsHighBitDepth => BitDepth > 8;

    public bool CanTransferColorMeaning => ColorMeaning != PngColorMeaning.NotTransferable;

    public PngSourceEncodingFacts SourceEncodingFacts => new(ColorMeaning, Orientation);

    public PngImageFacts CreateFacts(PngTransparencyFacts? transparency = null) =>
        new(Width, Height, FrameCount, PixelCount, BitDepth, ColorType, transparency);
}

internal static class PngChunkInspector
{
    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const int SignatureLength = 8;
    private const int IhdrDataLength = 13;
    private const int ActlDataLength = 8;
    private const int MaxChunksBeforeImageData = 4096;
    private const int MaxExifEntries = 1024;
    private const int ExifEntryLength = 12;
    private const int ExifOrientationTag = 0x0112;
    private const int ExifShortType = 3;
    private const int IdentityOrientation = 1;
    private const int MaximumOrientation = 8;

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
        var colorChunks = default(PngColorChunks);
        var orientation = PngPresentationOrientation.Identity;
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

            colorChunks = CollectColorChunk(chunk, colorChunks);
            if (chunk.HasType("eXIf"u8))
            {
                orientation = ReadOrientation(chunk);
            }

            if (chunk.HasType("IDAT"u8))
            {
                return TryCreatePreflight(
                    encoded,
                    header with
                    {
                        ColorMeaning = ResolveColorMeaning(colorChunks),
                        Orientation = orientation
                    },
                    declaredFrameCount,
                    out preflight);
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

    private static PngColorChunks CollectColorChunk(PngChunk chunk, PngColorChunks collected)
    {
        if (chunk.HasType("cICP"u8))
        {
            return collected with { HasCicp = true };
        }
        if (chunk.HasType("iCCP"u8))
        {
            return collected with { HasIccp = true };
        }
        if (chunk.HasType("sRGB"u8))
        {
            return collected with { HasSrgb = true };
        }
        if (chunk.HasType("gAMA"u8) || chunk.HasType("cHRM"u8))
        {
            return collected with { HasGamaOrChrm = true };
        }

        return collected;
    }

    private static PngColorMeaning ResolveColorMeaning(PngColorChunks collected)
    {
        if (collected.HasCicp)
        {
            return PngColorMeaning.NotTransferable;
        }
        if (collected.HasIccp)
        {
            return PngColorMeaning.IccProfile;
        }
        if (collected.HasSrgb)
        {
            return PngColorMeaning.DeclaredSrgb;
        }

        return collected.HasGamaOrChrm
            ? PngColorMeaning.NotTransferable
            : PngColorMeaning.AssumedSrgb;
    }

    private static PngPresentationOrientation ReadOrientation(PngChunk chunk)
    {
        var data = chunk.Data;
        if (!chunk.HasValidChecksum || data.Length < 8)
        {
            return PngPresentationOrientation.Unreadable;
        }

        bool littleEndian;
        if (data[0] == 0x49 && data[1] == 0x49)
        {
            littleEndian = true;
        }
        else if (data[0] == 0x4D && data[1] == 0x4D)
        {
            littleEndian = false;
        }
        else
        {
            return PngPresentationOrientation.Unreadable;
        }

        if (ReadUInt16(data[2..], littleEndian) != 42)
        {
            return PngPresentationOrientation.Unreadable;
        }

        var directoryOffset = ReadUInt32(data[4..], littleEndian);
        if (directoryOffset > int.MaxValue || directoryOffset + 2 > (uint)data.Length)
        {
            return PngPresentationOrientation.Unreadable;
        }

        var directory = data[(int)directoryOffset..];
        var entryCount = ReadUInt16(directory, littleEndian);
        if (entryCount > MaxExifEntries
            || 2 + (entryCount * ExifEntryLength) > directory.Length)
        {
            return PngPresentationOrientation.Unreadable;
        }

        for (var entry = 0; entry < entryCount; entry++)
        {
            var offset = 2 + (entry * ExifEntryLength);
            if (ReadUInt16(directory[offset..], littleEndian) != ExifOrientationTag)
            {
                continue;
            }

            if (ReadUInt16(directory[(offset + 2)..], littleEndian) != ExifShortType
                || ReadUInt32(directory[(offset + 4)..], littleEndian) != 1)
            {
                return PngPresentationOrientation.Unreadable;
            }

            var value = ReadUInt16(directory[(offset + 8)..], littleEndian);
            if (value is 0 or > MaximumOrientation)
            {
                return PngPresentationOrientation.Unreadable;
            }

            return value == IdentityOrientation
                ? PngPresentationOrientation.Identity
                : PngPresentationOrientation.NonDefault;
        }

        return PngPresentationOrientation.Identity;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> value, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(value)
            : BinaryPrimitives.ReadUInt16BigEndian(value);

    private static uint ReadUInt32(ReadOnlySpan<byte> value, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(value)
            : BinaryPrimitives.ReadUInt32BigEndian(value);

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

        preflight = header with { FrameCount = declaredFrameCount, HasAnimation = true };
        return PngAnimationValidator.TryValidate(encoded, preflight);
    }

    private readonly record struct PngColorChunks(
        bool HasCicp,
        bool HasIccp,
        bool HasSrgb,
        bool HasGamaOrChrm);
}
