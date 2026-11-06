using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;
using Availability.Domain.Enums;

namespace Availability.Application.UseCases;

public class GetRequestStatusUseCase : IGetReservationStatusUseCase
{
    private const string PublishFailedDescription = "No se pudo publicar la solicitud";
    private const string NeverPublishedDescription = "La solicitud no llegó a publicarse";
    private static readonly TimeSpan NeverPublishedThreshold = TimeSpan.FromMinutes(5);

    private readonly IReservationRequestRepository _reservationRequestRepository;
    private readonly IBookingClient _bookingClient;
    private readonly TimeProvider _timeProvider;

    public GetRequestStatusUseCase(
        IReservationRequestRepository reservationRequestRepository,
        IBookingClient bookingClient,
        TimeProvider timeProvider)
    {
        _reservationRequestRepository = reservationRequestRepository;
        _bookingClient = bookingClient;
        _timeProvider = timeProvider;
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

        if (request.Status == RequestStatus.PublishFailed)
        {
            return Result<RequestStatusDto>.Success(
                new RequestStatusDto(request.Id, RequestStatusNames.Failed, PublishFailedDescription, request.CreatedAt, request.UpdatedAt));
        }

        BookingReservationStatusDto? bookingStatus;
        try
        {
            bookingStatus = await _bookingClient.GetStatusByRequestIdAsync(request.Id, ct);
        }
        catch (ExternalServiceUnavailableException)
        {
            return Result<RequestStatusDto>.Failure(AppError.Unavailable);
        }

        if (bookingStatus is null)
        {
            if (request.Status == RequestStatus.Received
                && _timeProvider.GetUtcNow().UtcDateTime - request.CreatedAt > NeverPublishedThreshold)
            {
                return Result<RequestStatusDto>.Success(
                    new RequestStatusDto(request.Id, RequestStatusNames.Failed, NeverPublishedDescription, request.CreatedAt, request.UpdatedAt));
            }

            return Result<RequestStatusDto>.Success(new RequestStatusDto(request.Id, RequestStatusNames.Pending, null, request.CreatedAt, request.UpdatedAt));
        }

        return Result<RequestStatusDto>.Success(
            new RequestStatusDto(request.Id, bookingStatus.Status.ToUpperInvariant(), bookingStatus.Description, request.CreatedAt, request.UpdatedAt));
    }
}
