using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;
using Availability.Domain.Entities;
using Availability.Domain.Enums;

namespace Availability.Application.UseCases;

public class CreateReservationRequestUseCase : ICreateReservationRequestUseCase
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
        Guid userId, int eventId, int seatId, CancellationToken ct)
    {
        // NOTA IMPORTANTE (Responsabilidad de Infrastructure):
        // Los parámetros eventId y seatId son IDs externos (int) del cliente.
        // Estos deben convertirse a Guid internos usando:
        // - IEventRepository.GetByExternalIdAsync(eventId, ct)
        // - ISeatRepository.GetByExternalIdsAsync(eventId, seatId, ct)
        // 
        // Por ahora, usamos una conversión temporal. El equipo de Infrastructure
        // debe reemplazar esto con la lógica real de mapeo.

        var eventIdGuid = ConvertExternalEventIdToGuid(eventId);
        var seatIdGuid = ConvertExternalSeatIdToGuid(seatId);

        if (!await _seatRepository.ExistsAsync(eventIdGuid, seatIdGuid, ct))
        {
            return Result<RequestReceivedDto>.Failure(AppError.NotFound);
        }

        var request = new ReservationRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventId = eventIdGuid,
            SeatId = seatIdGuid,
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

    /// <summary>
    /// Conversión temporal de ID externo (int) a Guid.
    /// Esta es una implementación provisional. El equipo de Infrastructure debe
    /// reemplazarla con GetByExternalIdAsync() en IEventRepository.
    /// </summary>
    private static Guid ConvertExternalEventIdToGuid(int eventId)
    {
        // TODO: Implementar GetByExternalIdAsync() en IEventRepository
        var bytes = new byte[16];
        BitConverter.GetBytes(1).CopyTo(bytes, 0);
        BitConverter.GetBytes(eventId).CopyTo(bytes, 12);
        return new Guid(bytes);
    }

    /// <summary>
    /// Conversión temporal de ID externo (int) a Guid.
    /// Esta es una implementación provisional. El equipo de Infrastructure debe
    /// reemplazarla con GetByExternalIdsAsync() en ISeatRepository.
    /// </summary>
    private static Guid ConvertExternalSeatIdToGuid(int seatId)
    {
        // TODO: Implementar GetByExternalIdsAsync() en ISeatRepository
        var bytes = new byte[16];
        BitConverter.GetBytes(2).CopyTo(bytes, 0);
        BitConverter.GetBytes(seatId).CopyTo(bytes, 12);
        return new Guid(bytes);
    }
}
