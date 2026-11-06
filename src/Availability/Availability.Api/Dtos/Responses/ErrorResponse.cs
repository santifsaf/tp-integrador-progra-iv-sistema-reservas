namespace Availability.Api.DTOs.Responses;

/// <summary>
/// DTO para respuestas de error estándar de la API.
/// Proporciona información consistente sobre errores HTTP.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Código de error interno de la aplicación (no el código HTTP).
    /// Ej: "INVALID_EVENT_ID", "SEAT_UNAVAILABLE", "UNAUTHORIZED"
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Mensaje descriptivo del error en lenguaje natural.
    /// Ej: "El evento especificado no existe" 
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Identificador único de correlación para rastrear la solicitud.
    /// Permite vincular esta respuesta de error con los logs del servidor.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Detalles adicionales del error (opcional).
    /// Útil para proporcionar más contexto sobre qué salió mal.
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Constructor por defecto.
    /// </summary>
    public ErrorResponse()
    {
    }

    /// <summary>
    /// Constructor con parámetros básicos.
    /// </summary>
    /// <param name="code">Código de error interno</param>
    /// <param name="message">Mensaje descriptivo</param>
    /// <param name="correlationId">ID de correlación</param>
    public ErrorResponse(string code, string message, string correlationId)
    {
        Code = code;
        Message = message;
        CorrelationId = correlationId;
    }

    /// <summary>
    /// Constructor con parámetros completos incluidos detalles.
    /// </summary>
    /// <param name="code">Código de error interno</param>
    /// <param name="message">Mensaje descriptivo</param>
    /// <param name="correlationId">ID de correlación</param>
    /// <param name="details">Detalles adicionales</param>
    public ErrorResponse(string code, string message, string correlationId, string? details)
    {
        Code = code;
        Message = message;
        CorrelationId = correlationId;
        Details = details;
    }
}
