using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WebHealth.IntegrationTests.Support;

internal sealed class MultipleCollectionWarningRecorder : ILoggerProvider
{
    private int count;

    public int Count => Volatile.Read(ref count);

    public ILogger CreateLogger(string categoryName) => new Recorder(this);

    public void Dispose()
    {
    }

    private void Record(EventId eventId)
    {
        if (eventId.Id == RelationalEventId.MultipleCollectionIncludeWarning.Id)
        {
            Interlocked.Increment(ref count);
        }
    }

    private sealed class Recorder(MultipleCollectionWarningRecorder owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => owner.Record(eventId);
    }
}
