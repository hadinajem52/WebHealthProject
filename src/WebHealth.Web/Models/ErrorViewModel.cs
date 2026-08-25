namespace WebHealth.Web.Models;

public sealed record ErrorViewModel(
    int StatusCode,
    string Title,
    string Message,
    string CorrelationId,
    string? RetryUrl = null)
{
    /// <summary>
    /// Whether to show the correlation reference. A 4xx tells the reader what to do next and
    /// needs no reference; a 5xx is a fault they cannot act on, and the reference is the only
    /// thing connecting what they saw to what was logged.
    /// </summary>
    public bool ShowReference => StatusCode >= 500;

    public static ErrorViewModel Create(
        int statusCode,
        string correlationId,
        string? retryUrl = null,
        string? missingRecord = null)
    {
        return statusCode switch
        {
            400 => new(statusCode, "Invalid request", "The request was invalid or expired. Reload the page and submit it again.", correlationId),
            403 => new(statusCode, "Access denied", "Your role does not include this action. Nothing was changed. If you need it, ask an Administrator to adjust your roles.", correlationId),
            404 => new(
                statusCode,
                missingRecord is null ? "Not found" : $"{Capitalize(missingRecord)} not found",
                $"This {missingRecord ?? "record"} does not exist, or it was deleted, or it is outside "
                + "what your role can see. Check the address, or return to the dashboard and "
                + "navigate to it again.",
                correlationId),
            405 => new(statusCode, "That action is not available here", "This address does not accept the action you sent. Return to the dashboard and use the page's own controls.", correlationId),
            408 or 504 => new(statusCode, "The request timed out", "The request took too long and was stopped. Nothing was changed. Try again.", correlationId, retryUrl),
            409 => new(statusCode, "Someone else changed this first", "This record changed after you opened it, so your change was not applied. Reload the page to see the current values, then reapply your change.", correlationId, retryUrl),
            413 => new(statusCode, "That was too large", "The data you submitted exceeds what this form accepts. Reduce it and try again.", correlationId),
            429 => new(statusCode, "Too many requests", "Too many requests arrived in a short time. Wait a moment, then try again.", correlationId, retryUrl),
            503 => new(statusCode, "Service unavailable", "A service this page depends on is unavailable. The request was not completed.", correlationId, retryUrl),
            _ => new(statusCode, "Something went wrong", "The request could not be completed safely. Try again; if it keeps happening, quote the reference below.", correlationId, retryUrl)
        };
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
