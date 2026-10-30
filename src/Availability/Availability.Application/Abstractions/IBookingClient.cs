using Availability.Application.Dtos;

namespace Availability.Application.Abstractions;

public interface IBookingClient
{
    Task<IReadOnlySet<Guid>> GetOccupiedSeatIdsAsync(Guid eventId, CancellationToken ct);

    Task<BookingReservationStatusDto?> GetStatusByRequestIdAsync(Guid requestId, CancellationToken ct);
}
