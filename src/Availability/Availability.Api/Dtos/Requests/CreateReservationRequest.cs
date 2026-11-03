using System.ComponentModel.DataAnnotations;

namespace Availability.Api.DTOs.Requests;

/// <summary>
/// DTO para solicitar la creación de una reserva (1 asiento por reserva).
/// </summary>
public class CreateReservationRequest
{
    /// <summary>
    /// Identificador del evento (Guid).
    /// </summary>
    [Required(ErrorMessage = "EventId es requerido")]
    public Guid EventId { get; set; }

    /// <summary>
    /// Identificador del asiento a reservar (Guid).
    /// </summary>
    [Required(ErrorMessage = "SeatId es requerido")]
    public Guid SeatId { get; set; }
}
