using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.UseCases;

/// <summary>
/// Caso de uso para obtener la disponibilidad de asientos de un evento.
/// </summary>
public class GetSeatsAvailabilityUseCase : IGetSeatsAvailabilityUseCase
{
    private readonly GetSeatMapUseCase _seatMapUseCase;

    public GetSeatsAvailabilityUseCase(GetSeatMapUseCase seatMapUseCase)
    {
        _seatMapUseCase = seatMapUseCase;
    }

    /// <summary>
    /// Obtiene el mapa de asientos disponibles para un evento específico.
    /// </summary>
    /// <param name="eventId">ID del evento (Guid)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado con el mapa de asientos y su disponibilidad</returns>
    public async Task<Result<SeatMapDto>> ExecuteAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return await _seatMapUseCase.ExecuteAsync(eventId, cancellationToken);
    }
}
