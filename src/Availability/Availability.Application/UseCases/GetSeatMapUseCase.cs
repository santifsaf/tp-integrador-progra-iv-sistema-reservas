using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.UseCases;

public class GetSeatMapUseCase
{
    private const string Available = "AVAILABLE";
    private const string Occupied = "OCCUPIED";

    private readonly IEventRepository _eventRepository;
    private readonly ISeatRepository _seatRepository;
    private readonly IBookingClient _bookingClient;

    public GetSeatMapUseCase(
        IEventRepository eventRepository,
        ISeatRepository seatRepository,
        IBookingClient bookingClient)
    {
        _eventRepository = eventRepository;
        _seatRepository = seatRepository;
        _bookingClient = bookingClient;
    }

    public async Task<Result<SeatMapDto>> ExecuteAsync(Guid eventId, CancellationToken ct)
    {
        var evento = await _eventRepository.GetByIdAsync(eventId, ct);
        if (evento is null)
        {
            return Result<SeatMapDto>.Failure(AppError.NotFound);
        }

        var seats = await _seatRepository.GetByEventAsync(eventId, ct);

        IReadOnlySet<Guid> occupiedSeatIds;
        try
        {
            occupiedSeatIds = await _bookingClient.GetOccupiedSeatIdsAsync(eventId, ct);
        }
        catch (ExternalServiceUnavailableException)
        {
            return Result<SeatMapDto>.Failure(AppError.Unavailable);
        }

        var seatStatuses = seats
            .OrderBy(s => s.Label)
            .Select(s => new SeatStatusDto(s.Id, s.Label, occupiedSeatIds.Contains(s.Id) ? Occupied : Available))
            .ToList();

        var availableSeats = seatStatuses.Count(s => s.Status == Available);

        return Result<SeatMapDto>.Success(
            new SeatMapDto(evento.Id, evento.Name, seatStatuses.Count, availableSeats, seatStatuses));
    }
}
