namespace Availability.Api.DTOs.Responses;

/// <summary>
/// DTO para respuesta de solicitud de reserva.
/// </summary>
public class ReservationResponse
{
    /// <summary>
    /// Identificador único de la solicitud de reserva (GUID).
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Estado actual de la solicitud (ej: "Pending", "Confirmed", "Failed").
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Mensaje descriptivo de la respuesta.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
