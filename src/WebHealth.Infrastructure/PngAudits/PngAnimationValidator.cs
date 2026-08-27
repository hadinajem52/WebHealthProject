using System.Buffers.Binary;

namespace WebHealth.Infrastructure.PngAudits;

internal static class PngAnimationValidator
{
    private const int SignatureLength = 8;
    private const int MaxChunks = 4096;

    public static bool TryValidate(
        ReadOnlySpan<byte> encoded,
        PngChunkPreflight preflight)
    {
        var state = new PngAnimationState(
            preflight.Width,
            preflight.Height,
            preflight.FrameCount,
            preflight.ColorType);
        var offset = SignatureLength;
        for (var chunkCount = 0; chunkCount < MaxChunks; chunkCount++)
        {
            if (!PngChunkReader.TryRead(encoded, offset, out var chunk)
                || !chunk.HasValidChecksum
                || !state.Accept(chunk))
            {
                return false;
            }

            offset = chunk.NextOffset;
            if (chunk.HasType("IEND"u8))
            {
                return state.IsComplete && offset == encoded.Length;
            }
        }

        return false;
    }
}

internal sealed class PngAnimationState
{
    private const int IhdrDataLength = 13;
    private const int ActlDataLength = 8;
    private const int FctlDataLength = 26;
    private readonly uint _canvasWidth;
    private readonly uint _canvasHeight;
    private readonly int _declaredFrameCount;
    private readonly byte _colorType;
    private uint _expectedSequence;
    private int _frameControlCount;
    private bool _hasHeader;
    private bool _hasAnimationControl;
    private bool _hasPalette;
    private bool _hasImageData;
    private bool _isImageDataClosed;
    private bool _hasCurrentFrame;
    private bool _hasCurrentFrameData;

    public PngAnimationState(
        int canvasWidth,
        int canvasHeight,
        int declaredFrameCount,
        byte colorType)
    {
        _canvasWidth = (uint)canvasWidth;
        _canvasHeight = (uint)canvasHeight;
        _declaredFrameCount = declaredFrameCount;
        _colorType = colorType;
    }

    public bool IsComplete { get; private set; }

    public bool Accept(PngChunk chunk)
    {
        if (chunk.HasType("IHDR"u8))
        {
            return AcceptHeader(chunk);
        }

        if (!_hasHeader || IsComplete)
        {
            return false;
        }

        if (chunk.HasType("acTL"u8))
        {
            return AcceptAnimationControl(chunk);
        }

        if (chunk.HasType("fcTL"u8))
        {
            return AcceptFrameControl(chunk);
        }

        if (chunk.HasType("IDAT"u8))
        {
            return AcceptImageData();
        }

        if (chunk.HasType("fdAT"u8))
        {
            return AcceptFrameData(chunk);
        }

        if (chunk.HasType("IEND"u8))
        {
            return AcceptEnd(chunk);
        }

        if (chunk.HasType("PLTE"u8))
        {
            return AcceptPalette(chunk);
        }

        return AcceptOther(chunk);
    }

    private bool AcceptHeader(PngChunk chunk)
    {
        if (_hasHeader || chunk.Length != IhdrDataLength)
        {
            return false;
        }

        _hasHeader = true;
        return true;
    }

    private bool AcceptAnimationControl(PngChunk chunk)
    {
        if (_hasAnimationControl
            || _hasImageData
            || chunk.Length != ActlDataLength
            || BinaryPrimitives.ReadUInt32BigEndian(chunk.Data) != _declaredFrameCount)
        {
            return false;
        }

        _hasAnimationControl = true;
        return true;
    }

    private bool AcceptFrameControl(PngChunk chunk)
    {
        if (!_hasAnimationControl
            || chunk.Length != FctlDataLength
            || _hasCurrentFrame && !_hasCurrentFrameData
            || !AcceptSequence(chunk.Data)
            || !HasValidFrameBounds(chunk.Data))
        {
            return false;
        }

        if (_hasImageData)
        {
            _isImageDataClosed = true;
        }

        _frameControlCount++;
        _hasCurrentFrame = true;
        _hasCurrentFrameData = false;
        return _frameControlCount <= _declaredFrameCount;
    }

    private bool HasValidFrameBounds(ReadOnlySpan<byte> data)
    {
        var width = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        var xOffset = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        var yOffset = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        var fitsCanvas = width > 0
            && height > 0
            && (ulong)xOffset + width <= _canvasWidth
            && (ulong)yOffset + height <= _canvasHeight;
        var validOperations = data[24] <= 2 && data[25] <= 1;
        if (_frameControlCount == 0 && !_hasImageData)
        {
            return fitsCanvas
                && width == _canvasWidth
                && height == _canvasHeight
                && xOffset == 0
                && yOffset == 0
                && validOperations;
        }

        return fitsCanvas && validOperations;
    }

    private bool AcceptImageData()
    {
        if (!_hasAnimationControl
            || _isImageDataClosed
            || _colorType == 3 && !_hasPalette)
        {
            return false;
        }

        _hasImageData = true;
        if (_hasCurrentFrame)
        {
            _hasCurrentFrameData = true;
        }

        return true;
    }

    private bool AcceptFrameData(PngChunk chunk)
    {
        if (!_hasAnimationControl
            || !_hasImageData
            || !_hasCurrentFrame
            || chunk.Length <= sizeof(uint)
            || !AcceptSequence(chunk.Data))
        {
            return false;
        }

        _isImageDataClosed = true;
        _hasCurrentFrameData = true;
        return true;
    }

    private bool AcceptSequence(ReadOnlySpan<byte> data)
    {
        var sequence = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (sequence != _expectedSequence)
        {
            return false;
        }

        _expectedSequence++;
        return true;
    }

    private bool AcceptEnd(PngChunk chunk)
    {
        IsComplete = chunk.Length == 0
            && _hasAnimationControl
            && _hasImageData
            && _hasCurrentFrame
            && _hasCurrentFrameData
            && _frameControlCount == _declaredFrameCount;
        return IsComplete;
    }

    private bool AcceptPalette(PngChunk chunk)
    {
        var paletteIsAllowed = _colorType is 2 or 3 or 6;
        if (_hasPalette
            || _hasImageData
            || !paletteIsAllowed
            || chunk.Length is 0 or > 768
            || chunk.Length % 3 != 0)
        {
            return false;
        }

        _hasPalette = true;
        return true;
    }

    private bool AcceptOther(PngChunk chunk)
    {
        if (_hasImageData)
        {
            _isImageDataClosed = true;
        }

        return (chunk.Type[0] & 0x20) != 0;
    }
}
