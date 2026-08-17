using System.Text.Json;

namespace WebHealth.Infrastructure.Registry;

internal static class MonitorIntervalOverride
{
    public const int MinimumSeconds = 60;
    public const int MaximumSeconds = 24 * 60 * 60;

    public static bool HasOverride(string settings) => GetSeconds(settings) is not null;

    public static int? GetSeconds(string settings)
    {
        using var document = JsonDocument.Parse(settings);
        return document.RootElement.TryGetProperty("intervalSeconds", out var value)
            && value.TryGetInt32(out var seconds)
            ? seconds
            : null;
    }

    public static string Serialize(int? seconds) =>
        seconds is null ? "{}" : JsonSerializer.Serialize(new { intervalSeconds = seconds.Value });

    public static bool IsValid(int seconds) =>
        seconds is >= MinimumSeconds and <= MaximumSeconds;
}
