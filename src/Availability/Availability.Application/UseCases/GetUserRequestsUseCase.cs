using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;
using Availability.Domain.Enums;

namespace Availability.Application.UseCases;

public class GetUserRequestsUseCase
{
    private const string Received = "RECEIVED";
    private const string Failed = "FAILED";

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
            .Select(r => new UserRequestSummaryDto(r.Id, r.EventId, r.SeatId, MapStatus(r.Status), r.CreatedAt))
            .ToList();

        return Result<IReadOnlyList<UserRequestSummaryDto>>.Success(result);
    }

    private static string MapStatus(RequestStatus status) => status switch
    {
        RequestStatus.Received or RequestStatus.Published => Received,
        RequestStatus.PublishFailed => Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
