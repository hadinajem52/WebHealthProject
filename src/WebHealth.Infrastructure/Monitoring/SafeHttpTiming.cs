using System.Diagnostics;
using System.Net.Http;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class SafeHttpTimingCollector
{
    public int? DnsDurationMs { get; set; }
    public int? ConnectDurationMs { get; set; }
    public int? TlsDurationMs { get; set; }
    public long? ConnectCompletedTimestamp { get; set; }
}

internal static class SafeHttpTimingOptions
{
    public static readonly HttpRequestOptionsKey<SafeHttpTimingCollector> Key = new("WebHealth.Timing");
}

internal static class SafeHttpTimingMath
{
    public static int ElapsedMs(long startTimestamp) => ElapsedMs(startTimestamp, Stopwatch.GetTimestamp());

    public static int ElapsedMs(long startTimestamp, long endTimestamp) =>
        (int)Math.Clamp(
            Math.Ceiling(Stopwatch.GetElapsedTime(startTimestamp, endTimestamp).TotalMilliseconds),
            0,
            int.MaxValue);
}
