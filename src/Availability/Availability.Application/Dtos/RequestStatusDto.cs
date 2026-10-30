namespace Availability.Application.Dtos;

public record RequestStatusDto(Guid RequestId, string Status, string? Description);
