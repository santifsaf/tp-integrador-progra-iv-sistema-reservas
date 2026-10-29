using Availability.Domain.Enums;

namespace Availability.Domain.Entities;

public class ReservationRequest
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid EventId { get; set; }

    public Guid SeatId { get; set; }

    public RequestStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
}
