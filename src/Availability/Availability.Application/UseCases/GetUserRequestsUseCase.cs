using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.UseCases;

public class GetUserRequestsUseCase : IGetUserRequestsUseCase
{
    private readonly IReservationRequestRepository _reservationRequestRepository;

    public GetUserRequestsUseCase(IReservationRequestRepository reservationRequestRepository)
    {
        _reservationRequestRepository = reservationRequestRepository;
    }

    public async Task<Result<IReadOnlyList<UserRequestSummaryDto>>> ExecuteAsync(Guid userId, CancellationToken ct)
    {
        var requests = await _reservationRequestRepository.GetByUserIdAsync(userId, ct);

        IReadOnlyList<UserRequestSummaryDto> result = requests
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new UserRequestSummaryDto(r.Id, r.EventId, r.SeatId, RequestStatusNames.FromLocal(r.Status), r.CreatedAt))
            .ToList();

        return Result<IReadOnlyList<UserRequestSummaryDto>>.Success(result);
    }
}
