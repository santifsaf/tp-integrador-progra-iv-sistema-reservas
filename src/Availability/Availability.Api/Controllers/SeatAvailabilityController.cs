using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Api.DTOs.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Availability.Api.Controllers;

/// <summary>
/// Controlador para gestionar consultas de disponibilidad de asientos.
/// </summary>
[ApiController]
[Route("api/events")]
[Produces("application/json")]
public class SeatAvailabilityController : ControllerBase
{
    private readonly IGetSeatsAvailabilityUseCase _getSeatsAvailabilityUseCase;
    private readonly ILogger<SeatAvailabilityController> _logger;

    public SeatAvailabilityController(
        IGetSeatsAvailabilityUseCase getSeatsAvailabilityUseCase,
        ILogger<SeatAvailabilityController> logger)
    {
        _getSeatsAvailabilityUseCase = getSeatsAvailabilityUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene la lista de asientos de un evento con su disponibilidad actual.
    /// </summary>
    /// <param name="eventId">Identificador del evento (int)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>
    /// 200 OK: Listado de asientos con su estado de disponibilidad
    /// 400 Bad Request: ID de evento inválido (debe ser mayor a 0)
    /// 404 Not Found: Evento no encontrado
    /// 503 Service Unavailable: Servicio de reservas no disponible
    /// </returns>
    /// <remarks>
    /// Endpoint público (sin autenticación requerida).
    /// 
    /// Ejemplo de request:
    /// ```
    /// GET /api/events/15/seats
    /// ```
    /// 
    /// Ejemplo de respuesta (200 OK):
    /// ```json
    /// {
    ///   "eventId": 15,
    ///   "seats": [
    ///     {
    ///       "seatId": 1,
    ///       "status": "AVAILABLE"
    ///     },
    ///     {
    ///       "seatId": 2,
    ///       "status": "OCCUPIED"
    ///     }
    ///   ]
    /// }
    /// ```
    /// </remarks>
    [HttpGet("{eventId:int}/seats")]
    [ProducesResponseType(typeof(IEnumerable<GetSeatsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IEnumerable<GetSeatsResponse>>> GetSeatsAvailability(
        [FromRoute] int eventId,
        CancellationToken cancellationToken)
    {
        // Validación: eventId debe ser mayor a 0
        if (eventId <= 0)
        {
            _logger.LogWarning(
                "Solicitud de asientos rechazada: eventId inválido. EventId={EventId}",
                eventId);
            return BadRequest(new { error = "EventId debe ser mayor a 0" });
        }

        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
        _logger.LogInformation(
            "Consultando disponibilidad de asientos | EventId={EventId} | CorrelationId={CorrelationId}",
            eventId,
            correlationId);

        try
        {
            // Delegar en el caso de uso
            var result = await _getSeatsAvailabilityUseCase.ExecuteAsync(eventId, cancellationToken);

            // Manejar resultado fallido
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Fallo al obtener asientos | Error={Error} | EventId={EventId} | CorrelationId={CorrelationId}",
                    result.Error,
                    eventId,
                    correlationId);

                if (result.Error == AppError.NotFound)
                {
                    return NotFound(new { error = "Evento no encontrado" });
                }

                if (result.Error == AppError.Unavailable)
                {
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        new { error = "Servicio de reservas no disponible" });
                }

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "Error al procesar la solicitud" });
            }

            var seatMap = result.Value;

            // Validar que no sea nulo (aunque result.IsSuccess debería garantizarlo)
            if (seatMap == null)
            {
                _logger.LogError(
                    "Resultado exitoso pero valor nulo | EventId={EventId} | CorrelationId={CorrelationId}",
                    eventId,
                    correlationId);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "Error al procesar la solicitud" });
            }

            // Mapear SeatMapDto a GetSeatsResponse
            var response = seatMap.Seats
                .Select(seat => new GetSeatsResponse
                {
                    SeatId = ExtractSeatIdFromGuid(seat.SeatId),
                    Status = seat.Status,
                    EventId = eventId
                })
                .ToList();

            _logger.LogInformation(
                "Asientos recuperados exitosamente | EventId={EventId} | Count={Count} | CorrelationId={CorrelationId}",
                eventId,
                response.Count,
                correlationId);

            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Solicitud cancelada | EventId={EventId} | CorrelationId={CorrelationId}",
                eventId,
                correlationId);
            return StatusCode(StatusCodes.Status408RequestTimeout, new { error = "Solicitud expirada" });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error no controlado al obtener asientos | EventId={EventId} | CorrelationId={CorrelationId}",
                eventId,
                correlationId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "Error interno del servidor" });
        }
    }

    /// <summary>
    /// Extrae el ID del asiento (int) a partir del GUID interno.
    /// 
    /// Esta es una función temporal que invierte la conversión realizada en el UseCase.
    /// En arquitecturas futuras, se utilizará un repositorio de mapeo bidireccional.
    /// </summary>
    /// <param name="seatGuid">GUID interno del asiento</param>
    /// <returns>ID externo del asiento (int)</returns>
    private static int ExtractSeatIdFromGuid(Guid seatGuid)
    {
        // Extrae los últimos 4 bytes del GUID como int (temporal para desarrollo)
        var bytes = seatGuid.ToByteArray();
        return BitConverter.ToInt32(bytes, 12);
    }
}
