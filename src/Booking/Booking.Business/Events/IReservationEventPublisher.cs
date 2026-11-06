using Booking.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Booking.Business.Events;

// Punto de enganche para el servicio de notificaciones (consigna 3h).
// Booking avisa cuando una reserva queda creada (Held) y cuando queda Confirmed.
//
// PENDIENTE con Juan y Kevin: cómo le llega a Notifications. La clave de Service Bus que
// pasó el profe está atada al topic de booking, así que por broker hace falta que el
// profe cree una suscripción "notifications" con filtro. La alternativa es HTTP directo.
// Cualquiera de las dos se implementa con esta interfaz sin tocar la lógica de negocio.
// Mientras tanto se usa LoggingReservationEventPublisher.
public interface IReservationEventPublisher
{
    Task PublishAsync(ReservationEventType type, Reservation reservation, CancellationToken ct);
}

public enum ReservationEventType
{
    ReservationCreated,
    ReservationConfirmed,
}

// Implementación provisoria: solo deja el evento en el log.
internal sealed class LoggingReservationEventPublisher(ILogger<LoggingReservationEventPublisher> logger)
    : IReservationEventPublisher
{
    public Task PublishAsync(ReservationEventType type, Reservation reservation, CancellationToken ct)
    {
        logger.LogInformation(
            "Evento {EventType} para la reserva {ReservationId} (sin publicador real configurado)",
            type, reservation.Id);
        return Task.CompletedTask;
    }
}
