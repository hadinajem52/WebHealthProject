namespace WebHealth.Infrastructure.PngAudits;

internal static class SvgContentDetector
{
    public const string FormatName = "SVG";
    private const int MaxPrologueBytes = 4096;

    public static bool IsSvg(ReadOnlySpan<byte> content)
    {
        var window = content.Length > MaxPrologueBytes
            ? content[..MaxPrologueBytes]
            : content;
        var offset = SkipByteOrderMark(window);
        while (true)
        {
            offset = SkipWhitespace(window, offset);
            if (offset >= window.Length || window[offset] != (byte)'<')
            {
                return false;
            }
            if (StartsWith(window, offset, "<svg"u8))
            {
                return IsNameBoundary(window, offset + 4);
            }
            if (StartsWith(window, offset, "<!--"u8))
            {
                if (!TrySkipPast(window, offset + 4, "-->"u8, out offset)) return false;
                continue;
            }
            if (StartsWith(window, offset, "<?"u8) || StartsWith(window, offset, "<!"u8))
            {
                if (!TrySkipPast(window, offset + 2, ">"u8, out offset)) return false;
                continue;
            }

            return false;
        }
    }

    private static int SkipByteOrderMark(ReadOnlySpan<byte> content) =>
        StartsWith(content, 0, [0xEF, 0xBB, 0xBF]) ? 3 : 0;

    private static int SkipWhitespace(ReadOnlySpan<byte> content, int offset)
    {
        while (offset < content.Length && IsWhitespace(content[offset]))
        {
            offset++;
        }

        return offset;
    }

    private static bool IsWhitespace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static bool StartsWith(
        ReadOnlySpan<byte> content,
        int offset,
        ReadOnlySpan<byte> value)
    {
        if (offset < 0 || offset + value.Length > content.Length)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (ToLowerAscii(content[offset + index]) != ToLowerAscii(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static byte ToLowerAscii(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;

    private static bool IsNameBoundary(ReadOnlySpan<byte> content, int offset) =>
        offset < content.Length
        && (IsWhitespace(content[offset])
            || content[offset] is (byte)'>' or (byte)'/');

    private static bool TrySkipPast(
        ReadOnlySpan<byte> content,
        int offset,
        ReadOnlySpan<byte> terminator,
        out int next)
    {
        for (var index = offset; index + terminator.Length <= content.Length; index++)
        {
            if (StartsWith(content, index, terminator))
            {
                next = index + terminator.Length;
                return true;
            }
        }

        next = content.Length;
        return false;
    }
}
