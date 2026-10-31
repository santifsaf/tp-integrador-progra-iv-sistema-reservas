namespace Booking.Api.Errors;

// Permite integrar la consulta antes del repositorio EF sin simular datos
// en producción ni responder un 404 que Availability interpretaría como pendiente.
internal sealed class ReservationRepositoryNotConfiguredException()
    : InvalidOperationException("IReservationRepository todavía no está registrado en Booking.Api.");
