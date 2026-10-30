using Availability.Domain.Entities;
using Availability.Domain.Enums;

namespace Availability.Application.Abstractions;

public interface IReservationRequestRepository
{
    Task SaveAsync(ReservationRequest request, CancellationToken ct);

    Task UpdateStatusAsync(Guid id, RequestStatus status, CancellationToken ct);

    Task<ReservationRequest?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ReservationRequest>> GetByUserIdAsync(Guid userId, CancellationToken ct);
}
