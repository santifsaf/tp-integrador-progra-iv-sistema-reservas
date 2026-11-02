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
    /// 
    /// NOTA: eventId y seatId son IDs externos (int) del cliente.
    /// La conversión a Guid interno ocurre en la capa Application/Infrastructure
    /// mediante los métodos GetByExternalIdAsync() del repositorio.
    /// </summary>
    /// <param name="userId">ID del usuario que realiza la reserva (del JWT, en formato Guid)</param>
    /// <param name="eventId">ID externo del evento (int) - compatibilidad con API de Eventos</param>
    /// <param name="seatId">ID externo del asiento (int) - compatibilidad con API de Asientos</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado con el ID de la solicitud y su estado inicial (Guid) y status</returns>
    Task<Result<RequestReceivedDto>> ExecuteAsync(Guid userId, int eventId, int seatId, CancellationToken cancellationToken);
}
