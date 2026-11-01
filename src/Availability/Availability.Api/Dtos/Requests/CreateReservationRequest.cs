using System.ComponentModel.DataAnnotations;

namespace Availability.Api.DTOs.Requests;

/// <summary>
/// DTO para solicitar la creación de una reserva (1 asiento por reserva).
/// Contiene validaciones automáticas para EventId y SeatId.
/// </summary>
public class CreateReservationRequest
{
    /// <summary>
    /// Identificador del evento (debe ser > 0).
    /// Recibido desde el cliente en formato int (compatibilidad con API de Eventos).
    /// </summary>
    [Required(ErrorMessage = "EventId es requerido")]
    [Range(1, int.MaxValue, ErrorMessage = "EventId debe ser mayor a 0")]
    public int EventId { get; set; }

    /// <summary>
    /// Identificador del asiento a reservar (debe ser > 0).
    /// Recibido desde el cliente en formato int (compatibilidad con API de Asientos).
    /// </summary>
    [Required(ErrorMessage = "SeatId es requerido")]
    [Range(1, int.MaxValue, ErrorMessage = "SeatId debe ser mayor a 0")]
    public int SeatId { get; set; }
}
