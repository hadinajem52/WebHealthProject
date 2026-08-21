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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            logger.LogDebug(
                "HTTP {RequestMethod} {RequestPath} was canceled by the client.",
                context.Request.Method,
                context.Request.Path);
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
