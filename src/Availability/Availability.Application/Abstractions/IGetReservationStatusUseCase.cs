using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.Abstractions;

/// <summary>
/// Contrato para el caso de uso de obtener el estado de una solicitud de reserva.
/// </summary>
public interface IGetReservationStatusUseCase
{
    /// <summary>
    /// Obtiene el estado actual de una solicitud de reserva específica.
    /// </summary>
    /// <param name="requestId">ID de la solicitud de reserva</param>
    /// <param name="userId">ID del usuario que realiza la consulta (del JWT, para validación)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado con el estado actual de la solicitud</returns>
    Task<Result<RequestStatusDto>> ExecuteAsync(Guid requestId, Guid userId, CancellationToken cancellationToken);
}
