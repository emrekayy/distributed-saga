using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Ordering.UnitTests.Fakes;

/// <summary>Log mesajlarını bellekte toplar; log çıktısını doğrulayan testler için.</summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => [.. _messages];

    public ILogger CreateLogger(string categoryName) => new ListLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class ListLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception));
    }
}
