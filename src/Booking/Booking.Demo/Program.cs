using System.Text;
using Booking.Api.Messaging;
using Booking.Business;
using Booking.Business.Events;
using Booking.Business.Payments;
using Booking.Data;
using Booking.Demo;
using Booking.Demo.Simulation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

// SIMULADOR DE BOOKING
//
// Corre la lógica REAL de Booking (ReservationRequestProcessor, el job de vencimiento,
// los logs de auditoría) y reemplaza lo que todavía no existe o vive afuera:
//   Postgres           -> InMemoryReservationRepository (con las mismas restricciones)
//   Payment por gRPC   -> SimulatedPaymentClient (idempotente, con respuesta configurable)
//   Notifications      -> ConsoleNotificationPublisher
//   Broker             -> en memoria (por defecto) o Azure Service Bus real (--azure)
//
//   dotnet run --project src/Booking/Booking.Demo                     menú, broker en memoria
//   dotnet run --project src/Booking/Booking.Demo -- --auto           todos los escenarios
//   dotnet run --project src/Booking/Booking.Demo -- --azure          menú, Azure Service Bus real
//   dotnet run --project src/Booking/Booking.Demo -- --azure --auto
//
// Con --azure, la connection string se pasa por variable de entorno (nunca en el código):
//   PowerShell:  $env:Broker__ConnectionString = "Endpoint=sb://..."

Console.OutputEncoding = Encoding.UTF8;
var useAzure = args.Contains("--azure");

var builder = Host.CreateApplicationBuilder(args);

// Valores por defecto con la prioridad más baja: las variables de entorno los pisan.
builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
{
    InitialData = new Dictionary<string, string?>
    {
        // Tiempos cortos para poder ver el vencimiento sin esperar minutos.
        ["Reservations:HoldDuration"] = "00:00:15",
        ["Reservations:ExpirationInterval"] = "00:00:03",
        ["Broker:Topic"] = "grupo5-booking-topic",
        ["Broker:Subscription"] = "booking",
        ["Broker:MaxConcurrentCalls"] = "4",
    },
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.FormatterName = DemoConsoleFormatter.Name);
builder.Logging.AddConsoleFormatter<DemoConsoleFormatter, ConsoleFormatterOptions>();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Azure", LogLevel.Warning);

// Dobles en memoria (en el sistema real: Luis, Santi, Juan y Kevin).
builder.Services.AddSingleton<SeatCatalog>();
builder.Services.AddSingleton<InMemoryReservationRepository>();
builder.Services.AddSingleton<IReservationRepository>(sp => sp.GetRequiredService<InMemoryReservationRepository>());
builder.Services.AddSingleton<SimulatedPaymentClient>();
builder.Services.AddSingleton<IPaymentClient>(sp => sp.GetRequiredService<SimulatedPaymentClient>());
builder.Services.AddSingleton<IReservationEventPublisher, ConsoleNotificationPublisher>();

if (useAzure)
{
    if (string.IsNullOrWhiteSpace(builder.Configuration["Broker:ConnectionString"]))
    {
        Console.WriteLine("Falta la connection string de Azure Service Bus. En PowerShell:");
        Console.WriteLine("  $env:Broker__ConnectionString = \"Endpoint=sb://...\"");
        Console.WriteLine("y volvé a correr el comando en la misma ventana.");
        return;
    }

    // Lo real: la MISMA línea que va a ir en Booking.Api. Registra la lógica de negocio,
    // el job de vencimiento y el consumidor de Azure Service Bus.
    builder.Services.AddReservationRequestConsumer(builder.Configuration);
    builder.Services.AddSingleton<IDemoBroker, AzureDemoBroker>();
}
else
{
    // Lógica real + broker en memoria con N réplicas consumiendo.
    builder.Services.AddReservationProcessing(builder.Configuration);
    builder.Services.AddSingleton<InMemoryBroker>();
    builder.Services.AddSingleton<IDemoBroker>(sp => sp.GetRequiredService<InMemoryBroker>());
    builder.Services.AddHostedService<BookingReplicas>();
}

builder.Services.AddSingleton<DemoScenarios>();

using var host = builder.Build();
await host.StartAsync();

var demo = host.Services.GetRequiredService<DemoScenarios>();
if (args.Contains("--auto"))
    await demo.RunAllAsync();
else
    await demo.RunMenuAsync();

await host.StopAsync();
