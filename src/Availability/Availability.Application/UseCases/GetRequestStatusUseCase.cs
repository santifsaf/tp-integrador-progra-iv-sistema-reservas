using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;
using Availability.Domain.Enums;

namespace Availability.Application.UseCases;

public class GetRequestStatusUseCase
{
    private const string Pending = "PENDING";
    private const string Failed = "FAILED";
    private const string PublishFailedDescription = "No se pudo publicar la solicitud";

    private readonly IReservationRequestRepository _reservationRequestRepository;
    private readonly IBookingClient _bookingClient;

    public GetRequestStatusUseCase(
        IReservationRequestRepository reservationRequestRepository,
        IBookingClient bookingClient)
    {
        _reservationRequestRepository = reservationRequestRepository;
        _bookingClient = bookingClient;
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
                new RequestStatusDto(request.Id, Failed, PublishFailedDescription));
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
            return Result<RequestStatusDto>.Success(new RequestStatusDto(request.Id, Pending, null));
        }

        return Result<RequestStatusDto>.Success(
            new RequestStatusDto(request.Id, bookingStatus.Status.ToUpperInvariant(), bookingStatus.Description));
    }
}
