namespace Booking.Api.Messaging;

// Sección "Broker" del appsettings.json de Booking.
//
// La connection string NO va en el appsettings ni en el repo (el repo de entrega es
// público): se pasa por variable de entorno (Broker__ConnectionString) en el
// docker-compose, o con `dotnet user-secrets` en desarrollo.
public sealed class BrokerOptions
{
    public const string SectionName = "Broker";

    public string ConnectionString { get; set; } = string.Empty;

    // Tiene que ser EXACTAMENTE el EntityPath de la connection string que pasó el profe
    // ("grupo5-booking-topic"); si no coincide, el SDK tira una excepción al arrancar.
    public string Topic { get; set; } = string.Empty;

    public string Subscription { get; set; } = "booking";

    // Cuántos mensajes procesa en paralelo cada réplica.
    public int MaxConcurrentCalls { get; set; } = 4;
}
