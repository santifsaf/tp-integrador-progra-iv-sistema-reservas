namespace Availability.Api.DTOs.Responses;

/// <summary>
/// DTO para respuesta de consulta de asientos disponibles.
/// </summary>
public class GetSeatsResponse
{
    /// <summary>
    /// Identificador del asiento.
    /// </summary>
    public int SeatId { get; set; }

    /// <summary>
    /// Estado del asiento (ej: "Available", "Reserved", "Blocked").
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Identificador del evento al que pertenece el asiento.
    /// </summary>
    public int EventId { get; set; }
}
