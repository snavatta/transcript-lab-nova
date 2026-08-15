using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ClassTranscriber.Api.Tests;

public sealed record CapturedLogEntry(
    LogLevel Level,
    EventId EventId,
    IReadOnlyDictionary<string, object?> Properties,
    string Message,
    Exception? Exception);

public sealed class CapturingLoggerProvider(ConcurrentQueue<CapturedLogEntry> entries) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries);

    public void Dispose() { }

    private sealed class CapturingLogger(ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Where(pair => pair.Key != "{OriginalFormat}")
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>();

            entries.Enqueue(new CapturedLogEntry(
                logLevel,
                eventId,
                properties,
                formatter(state, exception),
                exception));
        }
    }
}
