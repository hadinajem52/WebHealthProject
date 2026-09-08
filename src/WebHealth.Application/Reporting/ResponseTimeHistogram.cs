namespace WebHealth.Application.Reporting;

public static class ResponseTimeHistogram
{
    public const int Version = 1;
    private static readonly int[] UpperBounds = [0, 100, 250, 500, 1000, 2500, 5000, 10000, 30000, 60000, 120000];
    public static int BucketCount => UpperBounds.Length + 1;

    public static int BucketIndex(int durationMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(durationMs);
        var index = Array.BinarySearch(UpperBounds, durationMs);
        return index >= 0 ? index : ~index;
    }

    public static double? EstimatePercentile(IReadOnlyList<long> counts, double percentile, int maximumDurationMs)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (counts.Count != BucketCount)
            throw new ArgumentException("Histogram bucket count does not match version 1.", nameof(counts));
        if (!double.IsFinite(percentile) || percentile < 0 || percentile > 1)
            throw new ArgumentOutOfRangeException(nameof(percentile));
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDurationMs);
        long total = 0;
        foreach (var count in counts)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            total = checked(total + count);
        }
        if (total == 0) return null;
        var rank = Math.Max(1, Math.Ceiling(total * percentile));
        long cumulative = 0;
        for (var index = 0; index < counts.Count; index++)
        {
            cumulative += counts[index];
            if (cumulative >= rank)
                return index < UpperBounds.Length ? Math.Min(UpperBounds[index], maximumDurationMs) : maximumDurationMs;
        }
        return maximumDurationMs;
    }
}
