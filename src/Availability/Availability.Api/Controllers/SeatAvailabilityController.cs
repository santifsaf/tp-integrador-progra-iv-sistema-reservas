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
    private readonly IGetEventsUseCase _getEventsUseCase;
    private readonly IGetSeatMapUseCase _getSeatMapUseCase;
    private readonly ILogger<SeatAvailabilityController> _logger;

    public SeatAvailabilityController(
        IGetEventsUseCase getEventsUseCase,
        IGetSeatMapUseCase getSeatMapUseCase,
        ILogger<SeatAvailabilityController> logger)
    {
        _getEventsUseCase = getEventsUseCase;
        _getSeatMapUseCase = getSeatMapUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene la lista de todos los eventos disponibles.
    /// Este endpoint es útil para obtener eventos sin depender del servicio de Booking.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <response code="200">Lista de eventos ordenados por fecha de inicio</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<EventSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EventSummaryDto>>> GetEvents(
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";

        _logger.LogInformation(
            "Obteniendo lista de eventos | CorrelationId={CorrelationId}",
            correlationId);

        try
        {
            var events = await _getEventsUseCase.ExecuteAsync(cancellationToken);

            _logger.LogInformation(
                "Lista de eventos obtenida exitosamente | EventCount={EventCount} | CorrelationId={CorrelationId}",
                events.Count,
                correlationId);

            return Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al obtener lista de eventos | CorrelationId={CorrelationId}",
                correlationId);

            var errorResponse = new ErrorResponse(
                "INTERNAL_SERVER_ERROR",
                "Error al obtener eventos",
                correlationId);

            return StatusCode(StatusCodes.Status500InternalServerError, errorResponse);
        }
    }

    /// <summary>
    /// Obtiene el mapa de asientos disponibles para un evento específico.
    /// Incluye información del evento, contador de asientos totales y disponibles,
    /// así como la lista de asientos con su etiqueta (ej: "A1", "B2") y estado.
    /// </summary>
    /// <param name="eventId">Identificador del evento (Guid)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <response code="200">Mapa de asientos con información del evento y estado de cada asiento</response>
    /// <response code="404">Evento no encontrado</response>
    /// <response code="503">No se pudo consultar a Booking</response>
    [HttpGet("{eventId:guid}/seats")]
    [ProducesResponseType(typeof(SeatMapDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
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