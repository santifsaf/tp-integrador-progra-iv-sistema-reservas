using Availability.Domain.Entities;

namespace Availability.Application.Abstractions;

public interface ISeatRepository
{
    Task<IReadOnlyList<Seat>> GetByEventAsync(Guid eventId, CancellationToken ct);

    Task<bool> ExistsAsync(Guid eventId, Guid seatId, CancellationToken ct);
}
