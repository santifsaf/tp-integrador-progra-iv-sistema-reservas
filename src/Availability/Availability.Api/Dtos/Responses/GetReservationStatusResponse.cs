namespace Availability.Api.DTOs.Responses;

/// <summary>
/// DTO para consultar el estado de una solicitud de reserva.
/// </summary>
public class GetReservationStatusResponse
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
    /// Fecha y hora de creación de la solicitud (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Fecha y hora de última actualización (UTC).
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
