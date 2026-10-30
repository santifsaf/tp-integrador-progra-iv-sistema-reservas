namespace Booking.Business;

// Sección "Reservations" del appsettings.json de Booking.
public sealed class ReservationsOptions
{
    public const string SectionName = "Reservations";

    // Cuánto tiempo queda retenido el asiento esperando el resultado del pago.
    public TimeSpan HoldDuration { get; set; } = TimeSpan.FromMinutes(2);

    // Cada cuánto corre el job que pasa las Held vencidas a Expired.
    public TimeSpan ExpirationInterval { get; set; } = TimeSpan.FromSeconds(30);
}
