using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.Abstractions;

/// <summary>
/// Contrato para el caso de uso de crear una solicitud de reserva.
/// </summary>
public interface ICreateReservationRequestUseCase
{
    /// <summary>
    /// Crea una nueva solicitud de reserva para un asiento específico.
    /// </summary>
    /// <param name="userId">ID del usuario que realiza la reserva (del JWT)</param>
    /// <param name="eventId">ID del evento (Guid interno del dominio)</param>
    /// <param name="seatId">ID del asiento (Guid interno del dominio)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado con el ID de la solicitud y su estado inicial</returns>
    Task<Result<RequestReceivedDto>> ExecuteAsync(Guid userId, Guid eventId, Guid seatId, CancellationToken cancellationToken);
}
