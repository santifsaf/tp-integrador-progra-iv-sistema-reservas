using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.UseCases;

public class GetRequestStatusUseCase : IGetReservationStatusUseCase
{
    private readonly IReservationRequestRepository _reservationRequestRepository;
    private readonly RequestStatusResolver _requestStatusResolver;

    public GetRequestStatusUseCase(
        IReservationRequestRepository reservationRequestRepository,
        RequestStatusResolver requestStatusResolver)
    {
        _reservationRequestRepository = reservationRequestRepository;
        _requestStatusResolver = requestStatusResolver;
    }

    public async Task<Result<RequestStatusDto>> ExecuteAsync(Guid requestId, Guid userId, CancellationToken ct)
    {
        var request = await _reservationRequestRepository.GetByIdAsync(requestId, ct);
        if (request is null)
        {
            return Result<RequestStatusDto>.Failure(AppError.NotFound);
        }

        if (request.UserId != userId)
        {
            return Result<RequestStatusDto>.Failure(AppError.Forbidden);
        }

        try
        {
            return Result<RequestStatusDto>.Success(await _requestStatusResolver.ResolveAsync(request, ct));
        }
        catch (ExternalServiceUnavailableException)
        {
            return Result<RequestStatusDto>.Failure(AppError.Unavailable);
        }
    }
}
