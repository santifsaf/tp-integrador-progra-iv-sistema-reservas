using Booking.Data;
using Booking.Data.Entities;
using Microsoft.Extensions.Logging;
using static Booking.Business.Auditing.AuditLog;

namespace Booking.Business.Queries;

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

            var description = reservation.Status == ReservationStatus.Rejected
                ? DescribeRejection(reservation.RejectionReason)
                : null;

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
        catch (Exception ex)
        {
            logger.LogError(ex, "Error consultando la reserva de la solicitud {RequestId}", requestId);
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
