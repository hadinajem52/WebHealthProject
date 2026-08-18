using System.Diagnostics;
using System.Net.Http;

namespace WebHealth.Infrastructure.Monitoring;

/// <summary>
/// Mutable per-attempt sink that <see cref="SafeHttpConnectionFactory"/> writes DNS, connect,
/// and TLS phase durations into. One instance is created per network attempt (including each
/// redirect hop) and attached to that attempt's <see cref="HttpRequestMessage.Options"/>, which
/// both <c>ConnectCallback</c> and <c>PlaintextStreamFilter</c> receive back via
/// <see cref="System.Net.Http.SocketsHttpHandler"/>'s per-attempt context — the correlated hook
/// this needs, unlike a handler-wide callback with no request context.
/// </summary>
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
