using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.Abstractions;

/// <summary>
/// Contrato para el caso de uso de obtener los asientos disponibles de un evento.
/// Este caso de uso consulta únicamente los asientos del evento sin consultar Booking.
/// </summary>
public interface IGetAvailableSeatsUseCase
{
    /// <summary>
    /// Obtiene la lista de todos los asientos disponibles para un evento específico.
    /// No incluye información sobre ocupación (eso viene de Booking).
    /// </summary>
    /// <param name="eventId">ID del evento (Guid)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado con la lista de asientos disponibles</returns>
    Task<Result<IReadOnlyList<AvailableSeatDto>>> ExecuteAsync(Guid eventId, CancellationToken cancellationToken);
}
