using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.Abstractions;

/// <summary>
/// Contrato para el caso de uso de obtener todas las solicitudes de reserva de un usuario.
/// </summary>
public interface IGetUserRequestsUseCase
{
    /// <summary>
    /// Obtiene todas las solicitudes de reserva realizadas por un usuario específico.
    /// Las solicitudes se devuelven ordenadas por fecha de creación descendente.
    /// </summary>
    /// <param name="userId">ID del usuario (Guid)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado con la lista de solicitudes del usuario</returns>
    Task<Result<IReadOnlyList<UserRequestSummaryDto>>> ExecuteAsync(Guid userId, CancellationToken cancellationToken);
}
