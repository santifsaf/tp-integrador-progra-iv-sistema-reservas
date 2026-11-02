using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.UseCases;

/// <summary>
/// Caso de uso para obtener la disponibilidad de asientos de un evento.
/// Convierte el ID externo (int) de evento al ID interno (Guid) y retorna el mapa de asientos.
/// </summary>
public class GetSeatsAvailabilityUseCase : IGetSeatsAvailabilityUseCase
{
    private readonly GetSeatMapUseCase _seatMapUseCase;

    public GetSeatsAvailabilityUseCase(GetSeatMapUseCase seatMapUseCase)
    {
        _seatMapUseCase = seatMapUseCase;
    }

    /// <summary>
    /// Obtiene el mapa de asientos disponibles para un evento específico usando el ID externo.
    /// </summary>
    /// <param name="eventId">ID externo del evento (int del servicio de Eventos)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado con el mapa de asientos y su disponibilidad</returns>
    public async Task<Result<SeatMapDto>> ExecuteAsync(int eventId, CancellationToken cancellationToken)
    {
        // TODO: Convertir int eventId a Guid interno
        // Por ahora, usamos un mapeo temporal
        // En fases posteriores, esto consultará un repositorio de mapeo externo -> interno

        // Genera un GUID consistente a partir del eventId externo para pruebas
        // ⚠️ Esto es solo para desarrollo/pruebas - en producción se requiere un repositorio de mapeo
        var internalEventId = new Guid($"00000000-0000-0000-0000-{eventId:000000000000}");

        return await _seatMapUseCase.ExecuteAsync(internalEventId, cancellationToken);
    }
}
