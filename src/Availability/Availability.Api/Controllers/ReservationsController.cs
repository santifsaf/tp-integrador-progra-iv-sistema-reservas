using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;
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
    private readonly IGetUserRequestsUseCase _getUserRequestsUseCase;
    private readonly ILogger<ReservationsController> _logger;

    public ReservationsController(
        ICreateReservationRequestUseCase createReservationRequestUseCase,
        IGetReservationStatusUseCase getReservationStatusUseCase,
        IGetUserRequestsUseCase getUserRequestsUseCase,
        ILogger<ReservationsController> logger)
    {
        _createReservationRequestUseCase = createReservationRequestUseCase;
        _getReservationStatusUseCase = getReservationStatusUseCase;
        _getUserRequestsUseCase = getUserRequestsUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene todas las solicitudes de reserva realizadas por el usuario autenticado.
    /// Las solicitudes se devuelven ordenadas por fecha de creación descendente (más recientes primero).
    /// Útil para mostrar el historial de reservas en el frontend.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>
    /// 200 OK: Lista de solicitudes de reserva del usuario (puede estar vacía)
    /// 401 Unauthorized: Token JWT ausente o inválido
    /// </returns>
    /// <remarks>
    /// Autenticación: Requerida (JWT Bearer token)
    /// 
    /// Ejemplo de request:
    /// ```
    /// GET /api/reservations
    /// Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cC...
    /// ```
    /// 
    /// Ejemplo de respuesta (200 OK):
    /// ```json
    /// [
    ///   {
    ///     "requestId": "a4f1b8e0-8902-4b21-9a2f-1c3d5e7f9b0a",
    ///     "eventId": "b5f2c9e1-9013-5c32-0b3f-2d4e6f8a0c1b",
    ///     "seatId": "c6f3d0e2-0124-6d43-1c4f-3e5f7f9b1d2c",
    ///     "status": "PENDING",
    ///     "createdAt": "2026-11-05T02:15:00Z"
    ///   }
    /// ]
    /// ```
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(List<UserRequestSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<UserRequestSummaryDto>>> GetMyRequests(
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";

        // Extraer userId del JWT
        var userIdString = User.GetUserId();
        if (!Guid.TryParse(userIdString, out var userId))
        {
            _logger.LogWarning(
                "Token con userId inválido | UserIdString={UserIdString} | CorrelationId={CorrelationId}",
                userIdString,
                correlationId);

            var errorResponse = new ErrorResponse(
                "INVALID_USER_ID",
                "El token contiene un userId inválido",
                correlationId);

            return Unauthorized(errorResponse);
        }

        _logger.LogInformation(
            "Obteniendo solicitudes de reserva del usuario | UserId={UserId} | CorrelationId={CorrelationId}",
            userId,
            correlationId);

        try
        {
            var result = await _getUserRequestsUseCase.ExecuteAsync(userId, cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Fallo al obtener solicitudes | Error={Error} | UserId={UserId} | CorrelationId={CorrelationId}",
                    result.Error,
                    userId,
                    correlationId);

                var internalErrorResponse = new ErrorResponse(
                    "INTERNAL_ERROR",
                    "Error al procesar la solicitud",
                    correlationId);
                return StatusCode(StatusCodes.Status500InternalServerError, internalErrorResponse);
            }

            var requests = result.Value;

            _logger.LogInformation(
                "Solicitudes obtenidas exitosamente | UserId={UserId} | Count={Count} | CorrelationId={CorrelationId}",
                userId,
                requests.Count,
                correlationId);

            return Ok(requests);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Solicitud cancelada | UserId={UserId} | CorrelationId={CorrelationId}",
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
            _logger.LogError(
                ex,
                "Error no controlado al obtener solicitudes | UserId={UserId} | CorrelationId={CorrelationId}",
                userId,
                correlationId);

            var errorResponse = new ErrorResponse(
                "INTERNAL_SERVER_ERROR",
                "Error interno del servidor",
                correlationId);

            return StatusCode(StatusCodes.Status500InternalServerError, errorResponse);
        }
    }

    /// <summary>
    /// Crea una nueva solicitud de reserva para un asiento específico.
    /// Responde inmediatamente con 202 Accepted sin esperar procesamiento asincrónico.
    /// </summary>
    /// <param name="request">DTO con EventId y SeatId (ambos como guid del servicio público)</param>
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
    ///   "eventId": "a4f1b8e0-8902-4b21-9a2f-1c3d5e7f9b0a",
    ///   "seatId": "c3d5e7f9-b0a4-4f1b-8e08-9024b219a2f"
    /// }
    /// ```
    /// 
    /// Ejemplo de respuesta (202 Accepted):
    /// ```json
    /// {
    ///   "requestId": "a4f1b8e0-8902-4b21-9a2f-1c3d5e7f9b0a",
    ///   "status": "PENDING",
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

        // Extraer userId del JWT
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
            request.EventId!.Value,
            request.SeatId!.Value,
            correlationIdLog);

        try
        {
            var result = await _createReservationRequestUseCase.ExecuteAsync(
                userId,
                request.EventId!.Value,
                request.SeatId!.Value,
                cancellationToken);

            // Manejar resultado fallido
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Fallo al crear solicitud de reserva | Error={Error} | UserId={UserId} | EventId={EventId} | SeatId={SeatId} | CorrelationId={CorrelationId}",
                    result.Error,
                    userId,
                    request.EventId!.Value,
                    request.SeatId!.Value,
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
                    request.EventId!.Value,
                    request.SeatId!.Value,
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
                request.EventId!.Value,
                request.SeatId!.Value,
                correlationIdLog);

            return Accepted($"/api/reservations/{response.RequestId}", response);
        }
        catch (OperationCanceledException)
        {
            var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogWarning(
                "Solicitud de reserva cancelada | UserId={UserId} | EventId={EventId} | SeatId={SeatId} | CorrelationId={CorrelationId}",
                userId,
                request.EventId!.Value,
                request.SeatId!.Value,
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
                request.EventId!.Value,
                request.SeatId!.Value,
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
            // NOTA: Validar UpdatedAt para evitar valores default (DateTime(0))
            var response = new GetReservationStatusResponse
            {
                RequestId = statusFromUseCase.RequestId.ToString("D"),
                Status = statusFromUseCase.Status,
                CreatedAt = statusFromUseCase.CreatedAt,
                UpdatedAt = statusFromUseCase.UpdatedAt > DateTime.MinValue 
                    ? statusFromUseCase.UpdatedAt 
                    : statusFromUseCase.CreatedAt  // Fallback a CreatedAt si UpdatedAt no está inicializado
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
    }
