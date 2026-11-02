using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Api.DTOs.Requests;
using Availability.Api.DTOs.Responses;
using Availability.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Availability.Api.Controllers;

/// <summary>
/// Controlador para gestionar solicitudes de reserva de asientos.
/// Requier autenticación JWT para todos los endpoints.
/// </summary>
[ApiController]
[Route("api/reservations")]
[Produces("application/json")]
[Authorize]
public class ReservationsController : ControllerBase
{
    private readonly ICreateReservationRequestUseCase _createReservationRequestUseCase;
    private readonly IGetReservationStatusUseCase _getReservationStatusUseCase;
    private readonly ILogger<ReservationsController> _logger;

    public ReservationsController(
        ICreateReservationRequestUseCase createReservationRequestUseCase,
        IGetReservationStatusUseCase getReservationStatusUseCase,
        ILogger<ReservationsController> logger)
    {
        _createReservationRequestUseCase = createReservationRequestUseCase;
        _getReservationStatusUseCase = getReservationStatusUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Crea una nueva solicitud de reserva para un asiento específico.
    /// Responde inmediatamente con 202 Accepted sin esperar procesamiento asincrónico.
    /// </summary>
    /// <param name="request">DTO con EventId y SeatId (ambos como int del servicio público)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>
    /// 202 Accepted: Solicitud aceptada, contiene RequestId para seguimiento posterior
    /// 400 Bad Request: Validación fallida (EventId o SeatId inválidos)
    /// 401 Unauthorized: Token JWT ausente o inválido
    /// 403 Forbidden: Usuario no autenticado o sin permisos
    /// 404 Not Found: Evento o asiento no existe
    /// 409 Conflict: Asiento ya está reservado
    /// 503 Service Unavailable: Servicio de publicación no disponible
    /// </returns>
    /// <remarks>
    /// Autenticación: Requerida (JWT Bearer token)
    /// 
    /// Ejemplo de request:
    /// ```
    /// POST /api/reservations
    /// Content-Type: application/json
    /// Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cC...
    /// 
    /// {
    ///   "eventId": 15,
    ///   "seatId": 42
    /// }
    /// ```
    /// 
    /// Ejemplo de respuesta (202 Accepted):
    /// ```json
    /// {
    ///   "requestId": "a4f1b8e0-8902-4b21-9a2f-1c3d5e7f9b0a",
    ///   "status": "PUBLISHED",
    ///   "message": "Solicitud de reserva aceptada. Use el requestId para seguimiento."
    /// }
    /// ```
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReservationResponse>> CreateReservation(
        [FromBody] CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        // Validación: ModelState
        if (!ModelState.IsValid)
        {
            var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogWarning(
                "Solicitud de reserva rechazada: validación fallida | CorrelationId={CorrelationId}",
                correlationId);
            var errorResponse = new ErrorResponse(
                "INVALID_MODEL",
                "Los datos de la solicitud son inválidos",
                correlationId,
                ModelState.ToString());
            return BadRequest(errorResponse);
        }

        // Validación: EventId y SeatId deben ser mayores a 0
        if (request.EventId <= 0 || request.SeatId <= 0)
        {
            var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogWarning(
                "Solicitud de reserva rechazada: IDs inválidos | EventId={EventId} | SeatId={SeatId} | CorrelationId={CorrelationId}",
                request.EventId,
                request.SeatId,
                correlationId);
            var errorResponse = new ErrorResponse(
                "INVALID_IDS",
                "EventId y SeatId deben ser mayores a 0",
                correlationId);
            return BadRequest(errorResponse);
        }

        // Extaer userId del JWT
        var userIdString = User.GetUserId();
        if (string.IsNullOrWhiteSpace(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogWarning(
                "Solicitud de reserva rechazada: UserId inválido en JWT | CorrelationId={CorrelationId}",
                correlationId);
            var errorResponse = new ErrorResponse(
                "INVALID_USER_ID",
                "UserId inválido en token JWT",
                correlationId);
            return Unauthorized(errorResponse);
        }

        var correlationIdLog = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
        _logger.LogInformation(
            "Creando solicitud de reserva | UserId={UserId} | EventId={EventId} | SeatId={SeatId} | CorrelationId={CorrelationId}",
            userId,
            request.EventId,
            request.SeatId,
            correlationIdLog);

        try
        {
            // Convertir int a Guid (temporalmente generamos un GUID a partir del int)
            // En producción, esto vendría de un repositorio
            var eventGuid = ConvertExternalEventIdToGuid(request.EventId);
            var seatGuid = ConvertExternalSeatIdToGuid(request.SeatId);

            // Invocar el caso de uso
            var result = await _createReservationRequestUseCase.ExecuteAsync(
                userId,
                eventGuid,
                seatGuid,
                cancellationToken);

            // Manejar resultado fallido
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Fallo al crear solicitud de reserva | Error={Error} | UserId={UserId} | EventId={EventId} | SeatId={SeatId} | CorrelationId={CorrelationId}",
                    result.Error,
                    userId,
                    request.EventId,
                    request.SeatId,
                    correlationIdLog);

                if (result.Error == AppError.NotFound)
                {
                    var errorResponse = new ErrorResponse(
                        "EVENT_OR_SEAT_NOT_FOUND",
                        "El evento o asiento especificado no existe",
                        correlationIdLog);
                    return NotFound(errorResponse);
                }

                if (result.Error == AppError.Forbidden)
                {
                    var errorResponse = new ErrorResponse(
                        "FORBIDDEN",
                        "No tiene permiso para realizar esta acción",
                        correlationIdLog);
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        errorResponse);
                }

                if (result.Error == AppError.Unavailable)
                {
                    var errorResponse = new ErrorResponse(
                        "SERVICE_UNAVAILABLE",
                        "El servicio de reservas no está disponible",
                        correlationIdLog);
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        errorResponse);
                }

                var internalErrorResponse = new ErrorResponse(
                    "INTERNAL_ERROR",
                    "Error al procesar la solicitud",
                    correlationIdLog);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    internalErrorResponse);
            }

            var requestFromUseCase = result.Value;
            if (requestFromUseCase == null)
            {
                _logger.LogError(
                    "Resultado exitoso pero valor nulo | UserId={UserId} | EventId={EventId} | SeatId={SeatId} | CorrelationId={CorrelationId}",
                    userId,
                    request.EventId,
                    request.SeatId,
                    correlationIdLog);

                var errorResponse = new ErrorResponse(
                    "NULL_RESULT",
                    "Resultado nulo inesperado",
                    correlationIdLog);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    errorResponse);
            }

            // Mapear resultado a DTO de respuesta
            var response = new ReservationResponse
            {
                RequestId = requestFromUseCase.RequestId.ToString("D"),
                Status = requestFromUseCase.Status,
                Message = "Solicitud de reserva aceptada. Use el requestId para seguimiento."
            };

            _logger.LogInformation(
                "Solicitud de reserva creada exitosamente | RequestId={RequestId} | UserId={UserId} | EventId={EventId} | SeatId={SeatId} | CorrelationId={CorrelationId}",
                response.RequestId,
                userId,
                request.EventId,
                request.SeatId,
                correlationIdLog);

            return Accepted($"/api/reservations/{response.RequestId}", response);
        }
        catch (OperationCanceledException)
        {
            var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogWarning(
                "Solicitud de reserva cancelada | UserId={UserId} | EventId={EventId} | SeatId={SeatId} | CorrelationId={CorrelationId}",
                userId,
                request.EventId,
                request.SeatId,
                correlationId);
            var errorResponse = new ErrorResponse(
                "REQUEST_TIMEOUT",
                "La solicitud expiró",
                correlationId);
            return StatusCode(StatusCodes.Status408RequestTimeout, errorResponse);
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogError(
                ex,
                "Error no controlado al crear solicitud de reserva | UserId={UserId} | EventId={EventId} | SeatId={SeatId} | CorrelationId={CorrelationId}",
                userId,
                request.EventId,
                request.SeatId,
                correlationId);
            var errorResponse = new ErrorResponse(
                "INTERNAL_SERVER_ERROR",
                "Error interno del servidor",
                correlationId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                errorResponse);
        }
    }

    /// <summary>
    /// Obtiene el estado actual de una solicitud de reserva.
    /// </summary>
    /// <param name="requestId">Identificador de la solicitud de reserva (GUID)</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>
    /// 200 OK: Estado actual de la solicitud
    /// 400 Bad Request: RequestId inválido (no es un GUID válido)
    /// 401 Unauthorized: Token JWT ausente o inválido
    /// 403 Forbidden: El usuario no es propietario de la solicitud
    /// 404 Not Found: Solicitud de reserva no existe
    /// 503 Service Unavailable: Servicio de booking no disponible
    /// </returns>
    /// <remarks>
    /// Autenticación: Requerida (JWT Bearer token)
    /// Autorización: Solo el propietario de la solicitud puede consultarla.
    /// 
    /// Ejemplo de request:
    /// ```
    /// GET /api/reservations/a4f1b8e0-8902-4b21-9a2f-1c3d5e7f9b0a
    /// Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cC...
    /// ```
    /// 
    /// Ejemplo de respuesta (200 OK):
    /// ```json
    /// {
    ///   "requestId": "a4f1b8e0-8902-4b21-9a2f-1c3d5e7f9b0a",
    ///   "status": "CONFIRMED",
    ///   "createdAt": "2026-10-29T10:30:00Z",
    ///   "updatedAt": "2026-10-29T10:35:00Z"
    /// }
    /// ```
    /// </remarks>
    [HttpGet("{requestId:guid}")]
    [ProducesResponseType(typeof(GetReservationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GetReservationStatusResponse>> GetReservationStatus(
        [FromRoute] Guid requestId,
        CancellationToken cancellationToken)
    {
        // Extraer userId del JWT
        var userIdString = User.GetUserId();
        if (string.IsNullOrWhiteSpace(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogWarning(
                "Consulta de estado rechazada: UserId inválido en JWT | CorrelationId={CorrelationId}",
                correlationId);
            var errorResponse = new ErrorResponse(
                "INVALID_USER_ID",
                "UserId inválido en token JWT",
                correlationId);
            return Unauthorized(errorResponse);
        }

        var correlationIdLog = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
        _logger.LogInformation(
            "Consultando estado de solicitud de reserva | RequestId={RequestId} | UserId={UserId} | CorrelationId={CorrelationId}",
            requestId,
            userId,
            correlationIdLog);

        try
        {
            // Invocar el caso de uso
            var result = await _getReservationStatusUseCase.ExecuteAsync(
                requestId,
                userId,
                cancellationToken);

            // Manejar resultado fallido
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Fallo al obtener estado de solicitud | Error={Error} | RequestId={RequestId} | UserId={UserId} | CorrelationId={CorrelationId}",
                    result.Error,
                    requestId,
                    userId,
                    correlationIdLog);

                if (result.Error == AppError.NotFound)
                {
                    var errorResponse = new ErrorResponse(
                        "RESERVATION_NOT_FOUND",
                        "Solicitud de reserva no encontrada",
                        correlationIdLog);
                    return NotFound(errorResponse);
                }

                if (result.Error == AppError.Forbidden)
                {
                    var errorResponse = new ErrorResponse(
                        "FORBIDDEN",
                        "No puede consultar el estado de esta solicitud",
                        correlationIdLog);
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        errorResponse);
                }

                if (result.Error == AppError.Unavailable)
                {
                    var errorResponse = new ErrorResponse(
                        "SERVICE_UNAVAILABLE",
                        "El servicio de booking no está disponible",
                        correlationIdLog);
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        errorResponse);
                }

                var internalErrorResponse = new ErrorResponse(
                    "INTERNAL_ERROR",
                    "Error al procesar la solicitud",
                    correlationIdLog);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    internalErrorResponse);
            }

            var statusFromUseCase = result.Value;
            if (statusFromUseCase == null)
            {
                _logger.LogError(
                    "Resultado exitoso pero valor nulo | RequestId={RequestId} | UserId={UserId} | CorrelationId={CorrelationId}",
                    requestId,
                    userId,
                    correlationIdLog);

                var errorResponse = new ErrorResponse(
                    "NULL_RESULT",
                    "Resultado nulo inesperado",
                    correlationIdLog);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    errorResponse);
            }

            // Mapear resultado a DTO de respuesta
            var response = new GetReservationStatusResponse
            {
                RequestId = statusFromUseCase.RequestId.ToString("D"),
                Status = statusFromUseCase.Status,
                CreatedAt = statusFromUseCase.CreatedAt,
                UpdatedAt = statusFromUseCase.UpdatedAt
            };

            _logger.LogInformation(
                "Estado de solicitud obtenido exitosamente | RequestId={RequestId} | Status={Status} | UserId={UserId} | CorrelationId={CorrelationId}",
                response.RequestId,
                response.Status,
                userId,
                correlationIdLog);

            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogWarning(
                "Consulta de estado cancelada | RequestId={RequestId} | UserId={UserId} | CorrelationId={CorrelationId}",
                requestId,
                userId,
                correlationId);
            var errorResponse = new ErrorResponse(
                "REQUEST_TIMEOUT",
                "La solicitud expiró",
                correlationId);
            return StatusCode(StatusCodes.Status408RequestTimeout, errorResponse);
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogError(
                ex,
                "Error no controlado al obtener estado de solicitud | RequestId={RequestId} | UserId={UserId} | CorrelationId={CorrelationId}",
                requestId,
                userId,
                correlationId);
            var errorResponse = new ErrorResponse(
                "INTERNAL_SERVER_ERROR",
                "Error interno del servidor",
                correlationId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                errorResponse);
        }
    }

    /// <summary>
    /// Convierten IDs externos (int) a GUIDs internos del dominio.
    /// Esta es una implementación temporal. En producción, estos vendría de repositorios.
    /// </summary>
    private static Guid ConvertExternalEventIdToGuid(int eventId)
    {
        // Crea un GUID determinístico a partir del ID externo
        // En producción, esto quedaría en un repositorio de mapeo
        var bytes = new byte[16];
        BitConverter.GetBytes(1).CopyTo(bytes, 0); // Namespace type
        BitConverter.GetBytes(eventId).CopyTo(bytes, 12); // El ID especifico
        return new Guid(bytes);
    }

    /// <summary>
    /// Convierte un SeatId externo (int) a un GUID interno.
    /// Esta es una implementación temporal.
    /// </summary>
    private static Guid ConvertExternalSeatIdToGuid(int seatId)
    {
        // Crea un GUID determinístico a partir del ID externo
        // En producción, esto quedaría en un repositorio de mapeo
        var bytes = new byte[16];
        BitConverter.GetBytes(2).CopyTo(bytes, 0); // Namespace type diferente
        BitConverter.GetBytes(seatId).CopyTo(bytes, 12); // El ID específico
        return new Guid(bytes);
    }
}
