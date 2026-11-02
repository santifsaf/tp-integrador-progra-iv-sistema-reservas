using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.Abstractions;

/// <summary>
/// Contrato para el caso de uso de obtener disponibilidad de asientos.
/// </summary>
public interface IGetSeatsAvailabilityUseCase
{
    /// <summary>
    /// Obtiene el mapa de asientos disponibles para un evento específico.
    /// </summary>
    /// <param name="eventId">ID externo del evento (int del servicio de Eventos)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado con el mapa de asientos y su disponibilidad</returns>
    Task<Result<SeatMapDto>> ExecuteAsync(int eventId, CancellationToken cancellationToken);
}
