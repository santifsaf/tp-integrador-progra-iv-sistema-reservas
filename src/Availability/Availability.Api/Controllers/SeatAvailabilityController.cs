using Availability.Api.DTOs.Responses;
using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;
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
    private readonly IGetSeatMapUseCase _getSeatMapUseCase;
    private readonly ILogger<SeatAvailabilityController> _logger;

    public SeatAvailabilityController(
        IGetSeatMapUseCase getSeatMapUseCase,
        ILogger<SeatAvailabilityController> logger)
    {
        _getSeatMapUseCase = getSeatMapUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene el mapa de asientos disponibles para un evento específico.
    /// Incluye información del evento, contador de asientos totales y disponibles,
    /// así como la lista de asientos con su etiqueta (ej: "A1", "B2") y estado.
    /// </summary>
    /// <param name="eventId">Identificador del evento (Guid)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>
    /// 200 OK: Mapa de asientos con información del evento y estado de cada asiento
    /// 404 Not Found: Evento no encontrado
    /// 503 Service Unavailable: Servicio de reservas no disponible
    /// </returns>
    [HttpGet("{eventId:guid}/seats")]
    [ProducesResponseType(typeof(SeatMapDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SeatMapDto>> GetSeatsAvailability(
        [FromRoute] Guid eventId,
        CancellationToken cancellationToken)
    {
        // Se declara una sola vez para todo el método
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";

        _logger.LogInformation(
            "Consultando disponibilidad de asientos | EventId={EventId} | CorrelationId={CorrelationId}",
            eventId,
            correlationId);

        try
        {
            // Delegar en el caso de uso
            var result = await _getSeatMapUseCase.ExecuteAsync(eventId, cancellationToken);

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

            _logger.LogInformation(
                "Asientos recuperados exitosamente | EventId={EventId} | Count={Count} | CorrelationId={CorrelationId}",
                eventId,
                seatMap.Seats.Count,
                correlationId);

            return Ok(seatMap);
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
    }