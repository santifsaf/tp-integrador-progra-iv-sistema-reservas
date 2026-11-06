using Availability.Domain.Entities;

namespace Availability.Application.Abstractions;

public interface IEventRepository
{
    Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken ct);

    Task<Event?> GetByIdAsync(Guid eventId, CancellationToken ct);
}
