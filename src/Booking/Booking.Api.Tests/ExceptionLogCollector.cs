using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booking.Api.Tests;

// Incluye Business y API: la misma excepción debe registrarse una sola vez,
// con los identificadores estructurados conservados por el manejador.
internal sealed class ExceptionLogCollector : ILoggerProvider
{
    public ConcurrentQueue<IReadOnlyDictionary<string, object?>> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) =>
        categoryName.StartsWith("Booking.", StringComparison.Ordinal)
            ? new ErrorLogger(Entries)
            : NullLogger.Instance;

    public void Dispose() { }

    private sealed class ErrorLogger(
        ConcurrentQueue<IReadOnlyDictionary<string, object?>> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && state is IEnumerable<KeyValuePair<string, object?>> properties)
                entries.Enqueue(properties.ToDictionary(property => property.Key, property => property.Value));
        }
    }
}
