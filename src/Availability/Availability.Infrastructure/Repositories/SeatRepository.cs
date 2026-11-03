using Availability.Application.Abstractions;
using Availability.Domain.Entities;
using Availability.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Availability.Infrastructure.Repositories;

public sealed class SeatRepository(AvailabilityDbContext db) : ISeatRepository
{
    public async Task<IReadOnlyList<Seat>> GetByEventAsync(Guid eventId, CancellationToken ct) =>
        await db.Seats
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.Label)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(Guid eventId, Guid seatId, CancellationToken ct) =>
        db.Seats
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId && x.Id == seatId, ct);
}
