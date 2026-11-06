using Availability.Application.Dtos;

namespace Availability.Application.Abstractions;

public interface IGetEventsUseCase
{
    Task<IReadOnlyList<EventSummaryDto>> ExecuteAsync(CancellationToken ct);
}
