using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;
using Availability.Domain.Entities;
using Availability.Domain.Enums;

namespace Availability.Application.UseCases;

public class CreateReservationRequestUseCase
{
    private readonly ISeatRepository _seatRepository;
    private readonly IReservationRequestRepository _reservationRequestRepository;
    private readonly IReservationPublisher _reservationPublisher;

    public CreateReservationRequestUseCase(
        ISeatRepository seatRepository,
        IReservationRequestRepository reservationRequestRepository,
        IReservationPublisher reservationPublisher)
    {
        _seatRepository = seatRepository;
        _reservationRequestRepository = reservationRequestRepository;
        _reservationPublisher = reservationPublisher;
    }

    public async Task<Result<RequestReceivedDto>> ExecuteAsync(
        Guid userId, Guid eventId, Guid seatId, CancellationToken ct)
    {
        if (!await _seatRepository.ExistsAsync(eventId, seatId, ct))
        {
            return Result<RequestReceivedDto>.Failure(AppError.NotFound);
        }

        var request = new ReservationRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventId = eventId,
            SeatId = seatId,
            Status = RequestStatus.Received,
            CreatedAt = DateTime.UtcNow
        };

        await _reservationRequestRepository.SaveAsync(request, ct);

        try
        {
            await _reservationPublisher.PublishAsync(request, ct);
        }
        catch (ExternalServiceUnavailableException)
        {
            await _reservationRequestRepository.UpdateStatusAsync(request.Id, RequestStatus.PublishFailed, ct);
            return Result<RequestReceivedDto>.Failure(AppError.Unavailable);
        }

        await _reservationRequestRepository.UpdateStatusAsync(request.Id, RequestStatus.Published, ct);

        return Result<RequestReceivedDto>.Success(
            new RequestReceivedDto(request.Id, RequestStatus.Published.ToString()));
    }
}
