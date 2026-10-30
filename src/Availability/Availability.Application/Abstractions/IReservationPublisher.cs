using Availability.Domain.Entities;

namespace Availability.Application.Abstractions;

public interface IReservationPublisher
{
    Task PublishAsync(ReservationRequest request, CancellationToken ct);
}
