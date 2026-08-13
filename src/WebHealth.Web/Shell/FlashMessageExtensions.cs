using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace WebHealth.Web.Shell;

/// <summary>
/// Stores flash messages in <see cref="ITempDataDictionary" /> so they survive a
/// single redirect and are removed once read.
/// </summary>
public static class FlashMessageExtensions
{
    /// <summary>The temp-data key holding the serialized message list.</summary>
    public const string TempDataKey = "WebHealth.FlashMessages";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Queues a message for the next rendered response.</summary>
    public static void AddFlashMessage(this ITempDataDictionary tempData, FlashLevel level, string text)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var messages = new List<FlashMessage>(Deserialize(tempData[TempDataKey] as string))
        {
            new(level, text)
        };

        tempData[TempDataKey] = JsonSerializer.Serialize(messages, SerializerOptions);
    }

    /// <summary>Reads and removes the queued messages.</summary>
    public static IReadOnlyList<FlashMessage> ReadFlashMessages(this ITempDataDictionary tempData)
    {
        ArgumentNullException.ThrowIfNull(tempData);

        return Deserialize(tempData[TempDataKey] as string);
    }

    private static IReadOnlyList<FlashMessage> Deserialize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<FlashMessage>>(payload, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
