namespace Availability.Api.DTOs.Requests;

/// <summary>
/// DTO para solicitar la creación de una reserva (1 asiento por reserva).
/// </summary>
public class CreateReservationRequest
{
    /// <summary>
    /// Identificador del evento.
    /// </summary>
    public int EventId { get; set; }

    /// <summary>
    /// Identificador del asiento a reservar.
    /// </summary>
    public int SeatId { get; set; }
}
