namespace WebHealth.Web.Middleware;

public sealed class SafeExceptionLoggingMiddleware(
    RequestDelegate next,
    ILogger<SafeExceptionLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Unhandled request failed with {ExceptionType}.",
                exception.GetType().Name);
            throw;
        }
    }
}
