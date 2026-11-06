using Booking.Business.Messages;

namespace Booking.Demo.Simulation;

// Lo que el simulador necesita de un broker: publicar solicitudes (hace de Availability)
// y esperar a que se terminen de procesar. Hay dos implementaciones:
//   - InMemoryBroker: todo en memoria, con 3 réplicas simuladas.
//   - AzureDemoBroker: publica en el topic real de Azure Service Bus y las consume
//     el ReservationRequestedConsumer real de Booking.Api.
public interface IDemoBroker
{
    string Description { get; }

    Task PublishAsync(ReservationRequestedMessage message);

    Task WaitUntilIdleAsync(TimeSpan timeout);

    string DeadLetterInfo { get; }
}
