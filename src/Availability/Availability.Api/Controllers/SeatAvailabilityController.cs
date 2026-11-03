using Availability.Api.DTOs.Responses;
using Availability.Application.Abstractions;
using Availability.Application.Common;
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
    [HttpGet("{eventId:int}/seats")]
    [ProducesResponseType(typeof(IEnumerable<GetSeatsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IEnumerable<GetSeatsResponse>>> GetSeatsAvailability(
        [FromRoute] int eventId,
        CancellationToken cancellationToken)
    {
        // Se declara una sola vez para todo el método
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";

        // Validación: eventId debe ser mayor a 0
        if (eventId <= 0)
        {
            _logger.LogWarning(
                "Solicitud de asientos rechazada: eventId inválido. EventId={EventId} | CorrelationId={CorrelationId}",
                eventId,
                correlationId);

            var errorResponse = new ErrorResponse(
                "INVALID_EVENT_ID",
                "El ID del evento debe ser mayor a 0",
                correlationId);

            return BadRequest(errorResponse);
        }

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
                    var errorResponse = new ErrorResponse(
                        "EVENT_NOT_FOUND",
                        "El evento especificado no existe",
                        correlationId);
                    return NotFound(errorResponse);
                }

                if (result.Error == AppError.Unavailable)
                {
                    var errorResponse = new ErrorResponse(
                        "SERVICE_UNAVAILABLE",
                        "El servicio de reservas no está disponible",
                        correlationId);
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, errorResponse);
                }

                var internalErrorResponse = new ErrorResponse(
                    "INTERNAL_ERROR",
                    "Error al procesar la solicitud",
                    correlationId);
                return StatusCode(StatusCodes.Status500InternalServerError, internalErrorResponse);
            }

            var seatMap = result.Value;

            if (seatMap == null)
            {
                _logger.LogError(
                    "Resultado exitoso pero valor nulo | EventId={EventId} | CorrelationId={CorrelationId}",
                    eventId,
                    correlationId);

                var errorResponse = new ErrorResponse(
                    "NULL_RESULT",
                    "Resultado nulo inesperado",
                    correlationId);
                return StatusCode(StatusCodes.Status500InternalServerError, errorResponse);
            }

            // Mapear SeatMapDto a GetSeatsResponse
            // NOTA: SeatId viene como Guid desde Application (lógica de mapeo temporal).
            // ExtractSeatIdFromGuid es una conversión provisional. El equipo de Infrastructure
            // debe resolver el mapeo Guid↔int en origen (en el repositorio/DTO).
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

            var errorResponse = new ErrorResponse(
                "REQUEST_TIMEOUT",
                "La solicitud expiró",
                correlationId);

            return StatusCode(StatusCodes.Status408RequestTimeout, errorResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error no controlado al obtener asientos | EventId={EventId} | CorrelationId={CorrelationId}",
                eventId,
                correlationId);

            var errorResponse = new ErrorResponse(
                "INTERNAL_SERVER_ERROR",
                "Error interno del servidor",
                correlationId);

            return StatusCode(StatusCodes.Status500InternalServerError, errorResponse);
        }
    }

    /// <summary>
    /// Converts internal GUID SeatId to external int.
    /// This is a TEMPORARY implementation. The Infrastructure team should resolve
    /// the mapping in the repository layer using GetByExternalIdAsync-like methods.
    /// TODO: Remove after Infrastructure implements proper Guid↔int mapping.
    /// </summary>
    private static int ExtractSeatIdFromGuid(Guid seatGuid)
    {
        var bytes = seatGuid.ToByteArray();
        return BitConverter.ToInt32(bytes, 12);
    }
}