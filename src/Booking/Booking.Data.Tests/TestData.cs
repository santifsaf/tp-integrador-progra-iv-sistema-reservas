using Booking.Data.Entities;

namespace Booking.Data.Tests;

internal static class TestData
{
    internal static Reservation BuildHeldReservation(
        Guid? id = null,
        Guid? requestId = null,
        Guid? userId = null,
        Guid? eventId = null,
        Guid? seatId = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        return new Reservation
        {
            Id = id ?? Guid.NewGuid(),
            RequestId = requestId ?? Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            EventId = eventId ?? Guid.NewGuid(),
            SeatId = seatId ?? Guid.NewGuid(),
            Status = ReservationStatus.Held,
            ExpiresAt = expiresAt ?? now.AddMinutes(2),
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now,
        };
    }

    internal static Reservation BuildRejectedReservation(
        Guid? id = null,
        Guid? requestId = null,
        Guid? userId = null,
        Guid? eventId = null,
        Guid? seatId = null,
        RejectionReason? reason = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        return new Reservation
        {
            Id = id ?? Guid.NewGuid(),
            RequestId = requestId ?? Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            EventId = eventId ?? Guid.NewGuid(),
            SeatId = seatId ?? Guid.NewGuid(),
            Status = ReservationStatus.Rejected,
            RejectionReason = reason ?? RejectionReason.SeatOccupied,
            ExpiresAt = null,
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now,
        };
    }
}