namespace Availability.Application.Dtos;

public record UserRequestSummaryDto(
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    string Status,
    DateTime CreatedAt);
