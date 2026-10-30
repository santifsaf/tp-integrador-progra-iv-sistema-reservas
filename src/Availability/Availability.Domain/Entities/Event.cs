namespace Availability.Domain.Entities;

public class Event
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; }
}
