namespace Availability.Application.Dtos;

public record EventSummaryDto(Guid EventId, string Name, DateTime StartsAt);
