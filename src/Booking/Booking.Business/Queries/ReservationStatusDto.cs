namespace Booking.Business.Queries;

// DTO de seguimiento acordado con Availability. Description es informativo;
// los clientes deben interpretar Status, no depender del texto de la descripción.
public sealed record ReservationStatusDto(string Status, string? Description);
