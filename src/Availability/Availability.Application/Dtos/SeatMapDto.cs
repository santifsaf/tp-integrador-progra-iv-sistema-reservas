namespace Availability.Application.Dtos;

public record SeatMapDto(
    Guid EventId,
    string EventName,
    int TotalSeats,
    int AvailableSeats,
    IReadOnlyList<SeatStatusDto> Seats);
