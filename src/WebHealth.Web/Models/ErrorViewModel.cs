namespace WebHealth.Web.Models;

public sealed record ErrorViewModel(
    int StatusCode,
    string Title,
    string Message,
    string CorrelationId)
{
    public static ErrorViewModel Create(int statusCode, string correlationId)
    {
        return statusCode switch
        {
            403 => new(statusCode, "Access denied", "You do not have permission to access this resource.", correlationId),
            404 => new(statusCode, "Page not found", "The requested resource could not be found.", correlationId),
            409 => new(statusCode, "Conflict", "The resource changed before this request completed.", correlationId),
            _ => new(statusCode, "Something went wrong", "The request could not be completed safely.", correlationId)
        };
    }
}
