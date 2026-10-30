using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.UseCases;

public class GetEventsUseCase
{
    private readonly IEventRepository _eventRepository;

    public GetEventsUseCase(IEventRepository eventRepository)
    {
        _eventRepository = eventRepository;
    }

    public async Task<Result<IReadOnlyList<EventSummaryDto>>> ExecuteAsync(CancellationToken ct)
    {
        var events = await _eventRepository.GetAllAsync(ct);

        IReadOnlyList<EventSummaryDto> result = events
            .OrderBy(e => e.StartsAt)
            .Select(e => new EventSummaryDto(e.Id, e.Name, e.StartsAt))
            .ToList();

        return Result<IReadOnlyList<EventSummaryDto>>.Success(result);
    }
}
