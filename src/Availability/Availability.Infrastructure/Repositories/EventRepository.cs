using Availability.Application.Abstractions;
using Availability.Domain.Entities;
using Availability.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Availability.Infrastructure.Repositories;

public sealed class EventRepository(AvailabilityDbContext db) : IEventRepository
{
    public async Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken ct) =>
        await db.Events
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<Event?> GetByIdAsync(Guid eventId, CancellationToken ct) =>
        db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eventId, ct);
}
