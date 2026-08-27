using System.Buffers.Binary;

namespace WebHealth.Infrastructure.PngAudits;

internal readonly ref struct PngChunk
{
    public PngChunk(
        uint length,
        ReadOnlySpan<byte> type,
        ReadOnlySpan<byte> data,
        uint checksum,
        int nextOffset)
    {
        Length = length;
        Type = type;
        Data = data;
        Checksum = checksum;
        NextOffset = nextOffset;
    }

    public uint Length { get; }

    public ReadOnlySpan<byte> Type { get; }

    public ReadOnlySpan<byte> Data { get; }

    public uint Checksum { get; }

    public int NextOffset { get; }

    public bool HasValidChecksum => PngCrc32.Calculate(Type, Data) == Checksum;

    public bool HasType(ReadOnlySpan<byte> expected) => Type.SequenceEqual(expected);
}

internal static class PngChunkReader
{
    private const int ChunkEnvelopeLength = 12;

    public static bool TryRead(
        ReadOnlySpan<byte> encoded,
        int offset,
        out PngChunk chunk)
    {
        chunk = default;
        if ((uint)offset > encoded.Length || encoded.Length - offset < ChunkEnvelopeLength)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(encoded[offset..]);
        var chunkEnd = (long)offset + ChunkEnvelopeLength + length;
        if (length > int.MaxValue || chunkEnd > encoded.Length)
        {
            return false;
        }

        var dataLength = (int)length;
        var type = encoded.Slice(offset + 4, 4);
        var data = encoded.Slice(offset + 8, dataLength);
        var checksum = BinaryPrimitives.ReadUInt32BigEndian(encoded[(offset + 8 + dataLength)..]);
        chunk = new(length, type, data, checksum, (int)chunkEnd);
        return true;
    }
}

internal static class PngCrc32
{
    private const uint Polynomial = 0xEDB88320;
    private static readonly uint[] Table = CreateTable();

    public static uint Calculate(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var checksum = Append(uint.MaxValue, type);
        return ~Append(checksum, data);
    }

    private static uint Append(uint checksum, ReadOnlySpan<byte> values)
    {
        foreach (var value in values)
        {
            checksum = Table[(byte)(checksum ^ value)] ^ (checksum >> 8);
        }

        return checksum;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0
                    ? value >> 1
                    : Polynomial ^ (value >> 1);
            }

            table[index] = value;
        }

        return table;
    }
}
