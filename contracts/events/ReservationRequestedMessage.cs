namespace Booking.Business.Messages;

public sealed record ReservationRequestedMessage(
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    Guid UserId,
    DateTimeOffset RequestedAt);