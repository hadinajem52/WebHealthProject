using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WebHealth.Web.Middleware;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class SafeExceptionLoggingMiddlewareTests
{
    [Fact]
    public async Task ClientCancellation_IsLoggedAsDebugAndMarkedAsClientClosedRequest()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token
        };
        var logger = new RecordingLogger<SafeExceptionLoggingMiddleware>();
        var middleware = new SafeExceptionLoggingMiddleware(
            _ => throw new OperationCanceledException(cancellation.Token),
            logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Debug);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task UnrelatedCancellation_IsLoggedAsErrorAndRethrown()
    {
        var context = new DefaultHttpContext();
        var logger = new RecordingLogger<SafeExceptionLoggingMiddleware>();
        var middleware = new SafeExceptionLoggingMiddleware(
            _ => throw new OperationCanceledException(),
            logger);

        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
