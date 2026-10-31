using Booking.Data;
using Booking.Data.Entities;
using Microsoft.Extensions.Logging;
using static Booking.Business.Auditing.AuditLog;

namespace Booking.Business.Queries;

// Repository: Business depende del contrato IReservationRepository y delega
// la persistencia en su implementación. La inyección por constructor permite
// integrar el repositorio real o un doble de pruebas sin cambiar la consulta.
public sealed class ReservationStatusQueryService(
    IReservationRepository repository,
    ILogger<ReservationStatusQueryService> logger)
{
    public async Task<ReservationStatusDto?> GetByRequestIdAsync(Guid requestId, CancellationToken ct)
    {
        using var scope = BeginChain(logger, requestId);
        Start(logger, requestId, "GetReservationStatus");

        try
        {
            var reservation = await repository.GetByRequestIdAsync(requestId, ct);
            if (reservation is null)
            {
                End(logger, requestId, "GetReservationStatus -> NotFound");
                return null;
            }

            // El contrato HTTP usa nombres estables en mayúsculas, independientes
            // del nombre del enum y de cómo se persistan esos valores en PostgreSQL.
            var status = reservation.Status switch
            {
                ReservationStatus.Held => "HELD",
                ReservationStatus.Confirmed => "CONFIRMED",
                ReservationStatus.Rejected => "REJECTED",
                ReservationStatus.Expired => "EXPIRED",
                _ => throw new InvalidOperationException("Estado de reserva no reconocido."),
            };

            // HELD describe la espera del resultado; no agrega un estado de pago
            // ni implica que Payment haya informado PAYMENT_PENDING.
            var description = reservation.Status switch
            {
                ReservationStatus.Held => "Pago en proceso",
                ReservationStatus.Rejected => DescribeRejection(reservation.RejectionReason),
                _ => null,
            };

            // La consulta informa el estado guardado: no cobra, confirma ni vence.
            // Esas transiciones pertenecen al procesamiento y al job de Booking.
            End(logger, requestId, $"GetReservationStatus -> {status}");
            return new ReservationStatusDto(status, description);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            End(logger, requestId, "GetReservationStatus -> Canceled");
            throw;
        }
        catch (Exception)
        {
            // El detalle de la excepción se registra una sola vez en el manejador
            // de la API. Business completa la auditoría y propaga el fallo.
            End(logger, requestId, "GetReservationStatus -> Error");
            // Solo la ausencia de reserva devuelve null. Un fallo de persistencia
            // debe seguir siendo un error para que Availability no muestre PENDING.
            throw;
        }
    }

    private static string DescribeRejection(RejectionReason? reason) => reason switch
    {
        RejectionReason.SeatOccupied => "El asiento está ocupado.",
        RejectionReason.PaymentFailed => "El pago fue rechazado.",
        RejectionReason.PaymentUnavailable => "El servicio de pago informó que el cobro no está disponible.",
        _ => "La reserva fue rechazada.",
    };
}
