using Microsoft.Extensions.Logging;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// One log record, as <see cref="CapturingLoggerProvider"/> saw it.
/// </summary>
public sealed record CapturedLog(string Category, LogLevel Level, EventId EventId, string Message);

/// <summary>
/// Keeps every log record the silo writes, so tests can assert operator-visible logs.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly Lock _lock = new();
    private readonly List<CapturedLog> _records = [];

    /// <summary>
    /// The records so far.
    /// </summary>
    public IReadOnlyList<CapturedLog> Records
    {
        get
        {
            lock (_lock)
            {
                return [.. _records];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(CapturedLog record)
    {
        lock (_lock)
        {
            _records.Add(record);
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (IsEnabled(logLevel))
            {
                provider.Add(new CapturedLog(category, logLevel, eventId, formatter(state, exception)));
            }
        }
    }
}
