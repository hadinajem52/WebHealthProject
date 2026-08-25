using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace WebHealth.Web.Shell;

public static class FlashMessageExtensions
{
    public const string TempDataKey = "WebHealth.FlashMessages";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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
