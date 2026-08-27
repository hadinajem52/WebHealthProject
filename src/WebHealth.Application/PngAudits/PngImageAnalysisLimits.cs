namespace WebHealth.Application.PngAudits;

public sealed record PngImageAnalysisLimits
{
    public PngImageAnalysisLimits(
        int maxEncodedBytes,
        int maxWidth,
        int maxHeight,
        long maxDecodedPixels,
        long maxDecodedMemoryBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEncodedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDecodedPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDecodedMemoryBytes);

        MaxEncodedBytes = maxEncodedBytes;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        MaxDecodedPixels = maxDecodedPixels;
        MaxDecodedMemoryBytes = maxDecodedMemoryBytes;
    }

    public int MaxEncodedBytes { get; }

    public int MaxWidth { get; }

    public int MaxHeight { get; }

    public long MaxDecodedPixels { get; }

    public long MaxDecodedMemoryBytes { get; }
}
