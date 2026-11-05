namespace Availability.Application.Dtos;

/// <summary>
/// Representa un asiento disponible para un evento.
/// Devuelve solo la información básica del asiento sin estado (ocupado/disponible).
/// </summary>
public record AvailableSeatDto(Guid SeatId, string Label);
