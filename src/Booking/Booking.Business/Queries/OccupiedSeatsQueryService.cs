using Booking.Data;
using Microsoft.Extensions.Logging;

namespace Booking.Business.Queries;

// N-Layer y Repository: Business coordina la lectura mediante el contrato de Data.
// El repositorio aplica el filtro de reservas activas; esta consulta no las modifica.
public sealed class OccupiedSeatsQueryService(
    IReservationRepository repository,
    ILogger<OccupiedSeatsQueryService> logger)
{
    public async Task<IReadOnlyList<Guid>> GetByEventIdAsync(Guid eventId, CancellationToken ct)
    {
        // Esta consulta no pertenece a una solicitud de reserva: se registra EventId
        // sin inventar un RequestId ni usar el evento como correlation id de reserva.
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["EventId"] = eventId });
        logger.LogInformation("[AUDIT] START GetOccupiedSeats | ServiceName=Booking EventId={EventId}", eventId);
        try
        {
            var seats = await repository.GetOccupiedSeatIdsAsync(eventId, ct);
            logger.LogInformation(
                "[AUDIT] END GetOccupiedSeats | ServiceName=Booking EventId={EventId} Count={Count}", eventId, seats.Count);
            return seats;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("[AUDIT] END GetOccupiedSeats Canceled | ServiceName=Booking EventId={EventId}", eventId);
            throw;
        }
        catch (Exception)
        {
            // El manejador de la API registra la excepción. Un fallo técnico se
            // propaga: devolver [] haría que Availability mostrara asientos libres.
            logger.LogInformation("[AUDIT] END GetOccupiedSeats Error | ServiceName=Booking EventId={EventId}", eventId);
            throw;
        }
    }
}
