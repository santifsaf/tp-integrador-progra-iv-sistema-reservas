using Booking.Business.Events;
using Booking.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Booking.Demo.Simulation;

// Hace de servicio de Notifications (punto 3h): muestra el aviso que le llegaría al usuario.
public sealed class ConsoleNotificationPublisher(ILoggerFactory loggers, SeatCatalog seats) : IReservationEventPublisher
{
    private readonly ILogger _logger = loggers.CreateLogger("Notifications");

    public Task PublishAsync(ReservationEventType type, Reservation reservation, CancellationToken ct)
    {
        var text = type == ReservationEventType.ReservationCreated
            ? "tu reserva fue creada; estamos procesando el pago"
            : "tu reserva está CONFIRMADA";
        _logger.LogInformation("Aviso al usuario: asiento {Seat}, {Text}", seats.Label(reservation.SeatId), text);
        return Task.CompletedTask;
    }
}
