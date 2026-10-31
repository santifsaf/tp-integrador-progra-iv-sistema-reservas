namespace Booking.Business.Messages;

public sealed record ReservationRequestedMessage(
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    Guid UserId,
    DateTimeOffset RequestedAt,
    long AmountCents,   // importe en centavos (int64)
    string Currency);   // código ISO 4217, hoy "ARS"