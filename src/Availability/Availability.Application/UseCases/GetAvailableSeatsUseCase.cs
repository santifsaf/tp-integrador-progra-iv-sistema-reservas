using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Application.UseCases;

/// <summary>
/// Implementación del caso de uso para obtener los asientos disponibles de un evento.
/// Consulta solo el repositorio de eventos y asientos, sin consultar Booking.
/// </summary>
public class GetAvailableSeatsUseCase : IGetAvailableSeatsUseCase
{
    private readonly IEventRepository _eventRepository;
    private readonly ISeatRepository _seatRepository;

    public GetAvailableSeatsUseCase(
        IEventRepository eventRepository,
        ISeatRepository seatRepository)
    {
        _eventRepository = eventRepository;
        _seatRepository = seatRepository;
    }

    public async Task<Result<IReadOnlyList<AvailableSeatDto>>> ExecuteAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        // Validar que el evento existe
        var evento = await _eventRepository.GetByIdAsync(eventId, cancellationToken);
        if (evento is null)
        {
            return Result<IReadOnlyList<AvailableSeatDto>>.Failure(AppError.NotFound);
        }

        // Obtener todos los asientos del evento
        var seats = await _seatRepository.GetByEventAsync(eventId, cancellationToken);

        // Mapear a DTO
        var availableSeats = seats
            .Select(s => new AvailableSeatDto(s.Id, s.Label))
            .ToList()
            .AsReadOnly();

        return Result<IReadOnlyList<AvailableSeatDto>>.Success(availableSeats);
    }
}
