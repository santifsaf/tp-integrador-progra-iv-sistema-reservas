using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;
using Availability.Domain.Entities;

namespace Availability.Application.UseCases;

public class GetUserRequestsUseCase : IGetUserRequestsUseCase
{
    private readonly IReservationRequestRepository _reservationRequestRepository;
    private readonly RequestStatusResolver _requestStatusResolver;

    public GetUserRequestsUseCase(
        IReservationRequestRepository reservationRequestRepository,
        RequestStatusResolver requestStatusResolver)
    {
        _reservationRequestRepository = reservationRequestRepository;
        _requestStatusResolver = requestStatusResolver;
    }

    public async Task<Result<IReadOnlyList<UserRequestSummaryDto>>> ExecuteAsync(Guid userId, CancellationToken ct)
    {
        var requests = await _reservationRequestRepository.GetByUserIdAsync(userId, ct);

        try
        {
            var result = await Task.WhenAll(requests
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => ToSummaryAsync(r, ct)));

            return Result<IReadOnlyList<UserRequestSummaryDto>>.Success(result);
        }
        catch (ExternalServiceUnavailableException)
        {
            return Result<IReadOnlyList<UserRequestSummaryDto>>.Failure(AppError.Unavailable);
        }
    }

    private async Task<UserRequestSummaryDto> ToSummaryAsync(ReservationRequest request, CancellationToken ct)
    {
        var status = await _requestStatusResolver.ResolveAsync(request, ct);

        return new UserRequestSummaryDto(request.Id, request.EventId, request.SeatId, status.Status, request.CreatedAt);
    }
}
