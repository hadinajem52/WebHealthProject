namespace WebHealth.Web.Models;

public sealed record ErrorViewModel(
    int StatusCode,
    string Title,
    string Message,
    string CorrelationId,
    string? RetryUrl = null)
{
    /// <summary>
    /// Builds a safe error state. A retry action is offered only for a
    /// dependency-unavailable response, where repeating the same request can
    /// reasonably succeed.
    /// </summary>
    /// <param name="statusCode">Response status code.</param>
    /// <param name="correlationId">Correlation reference shown to the user.</param>
    /// <param name="retryUrl">Verified local path of the original request, when one is available.</param>
    public static ErrorViewModel Create(int statusCode, string correlationId, string? retryUrl = null)
    {
        return statusCode switch
        {
            403 => new(statusCode, "Access denied", "You do not have permission to access this resource.", correlationId),
            404 => new(statusCode, "Page not found", "The requested resource could not be found.", correlationId),
            409 => new(statusCode, "Conflict", "The resource changed before this request completed.", correlationId),
            503 => new(statusCode, "Service unavailable", "A service this page depends on is unavailable. The request was not completed.", correlationId, retryUrl),
            _ => new(statusCode, "Something went wrong", "The request could not be completed safely.", correlationId)
        };
    }
}
