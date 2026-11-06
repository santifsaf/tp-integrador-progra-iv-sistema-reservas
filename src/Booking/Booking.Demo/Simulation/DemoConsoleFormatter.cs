using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Booking.Demo.Simulation;

// Formato de log corto para leer el simulador: hora, quién habla y el mensaje.
public sealed class DemoConsoleFormatter() : ConsoleFormatter(Name)
{
    public new const string Name = "demo";

    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopeProvider, TextWriter writer)
    {
        var message = entry.Formatter?.Invoke(entry.State, entry.Exception);
        if (string.IsNullOrEmpty(message)) return;

        var source = entry.Category[(entry.Category.LastIndexOf('.') + 1)..] switch
        {
            "ReservationRequestProcessor" => "Business",
            "ReservationExpirationService" => "Vencimiento",
            "ReservationExpirationWorker" => "Vencimiento",
            var other => other,
        };

        var color = entry.LogLevel switch
        {
            LogLevel.Warning => "\u001b[33m",
            LogLevel.Error or LogLevel.Critical => "\u001b[31m",
            _ when source.StartsWith("Booking-") => "\u001b[36m",
            _ when source == "Notifications" => "\u001b[32m",
            _ when source == "Payment" => "\u001b[35m",
            _ => "",
        };
        var reset = color.Length > 0 ? "\u001b[0m" : "";

        writer.WriteLine($"{color}{DateTime.Now:HH:mm:ss.fff}  {source,-13} {message}{reset}");
    }
}
