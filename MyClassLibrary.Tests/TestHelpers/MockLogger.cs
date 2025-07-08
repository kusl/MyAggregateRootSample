using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MyClassLibrary.Tests.TestHelpers;

public class MockLogger<T> : ILogger<T>
{
    private readonly ConcurrentBag<LogEntry> _logEntries = [];

    public IReadOnlyList<LogEntry> LogEntries => [.. _logEntries];

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _logEntries.Add(new LogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            Message = formatter(state, exception),
            Exception = exception,
            Timestamp = DateTime.UtcNow
        });
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool ContainsMessage(string partialMessage, LogLevel? logLevel = null)
    {
        return _logEntries.Any(e =>
            e.Message.Contains(partialMessage, StringComparison.OrdinalIgnoreCase) &&
            (logLevel == null || e.LogLevel == logLevel));
    }

    public void Clear() => _logEntries.Clear();
}

public class LogEntry
{
    public LogLevel LogLevel { get; init; }
    public EventId EventId { get; init; }
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
    public DateTime Timestamp { get; init; }
}