using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nordmann.LUI.Integration.Tests.Hosting;

/// <summary>
/// A logger of the host that keeps what it heard (ADR-0045): the tests of the warm-up read what the service told the
/// host — the level, the event and the exception.
/// </summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    /// <summary>What the loggers heard, in the order they heard it.</summary>
    internal ConcurrentQueue<(LogLevel Level, EventId Event, string Message, Exception? Exception)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Recorder(this);

    public void Dispose()
    {
    }

    /// <summary>The logger of one category; all of them write into the provider.</summary>
    private sealed class Recorder(RecordingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            provider.Entries.Enqueue((logLevel, eventId, formatter(state, exception), exception));
    }
}
