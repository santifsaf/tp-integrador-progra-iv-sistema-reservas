namespace Availability.Domain.Entities;

public class Seat
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string Label { get; set; } = string.Empty;
}
