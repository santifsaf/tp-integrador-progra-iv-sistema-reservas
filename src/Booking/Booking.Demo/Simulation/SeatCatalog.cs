namespace Booking.Demo.Simulation;

// Hace de "catálogo de Availability": un evento con asientos A1..A10.
// En el sistema real esto vive en la base de Availability; Booking solo recibe los ids.
public sealed class SeatCatalog
{
    public Guid EventId { get; } = Guid.NewGuid();

    public string EventName => "Recital de prueba";

    private readonly Dictionary<string, Guid> _byLabel =
        Enumerable.Range(1, 10).ToDictionary(i => $"A{i}", _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Labels => _byLabel.Keys;

    public bool TryGetSeatId(string label, out Guid seatId) => _byLabel.TryGetValue(label.Trim(), out seatId);

    public Guid SeatId(string label) => _byLabel[label];

    public string Label(Guid seatId) => _byLabel.FirstOrDefault(kv => kv.Value == seatId).Key ?? "?";
}
