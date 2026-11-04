using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.Abstractions;

/// <summary>
/// Contrato para el caso de uso de obtener el mapa de asientos de un evento.
/// </summary>
public interface IGetSeatMapUseCase
{
    /// <summary>
    /// Obtiene el mapa de asientos disponibles para un evento específico.
    /// </summary>
    /// <param name="eventId">ID del evento (Guid)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado con el mapa de asientos y su disponibilidad</returns>
    Task<Result<SeatMapDto>> ExecuteAsync(Guid eventId, CancellationToken cancellationToken);
}
