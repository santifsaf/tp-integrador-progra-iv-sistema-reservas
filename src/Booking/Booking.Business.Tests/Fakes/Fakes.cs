using Booking.Business.Events;
using Booking.Business.Payments;
using Booking.Data.Entities;

namespace Booking.Business.Tests.Fakes;

// Devuelve los resultados en el orden dado; si se acaban, repite el último.
// Así se puede simular "la primera vez timeout, la segunda éxito".
internal sealed class FakePaymentClient(params PaymentResult[] results) : IPaymentClient
{
    private int _calls;

    public int Calls => _calls;

    // Permite simular cosas que pasan mientras Booking espera a Payment
    // (por ejemplo, que el job de vencimiento corra en el medio).
    public Func<Reservation, Task>? During { get; set; }

    public async Task<PaymentResult> ChargeAsync(Reservation reservation, CancellationToken ct)
    {
        var call = Interlocked.Increment(ref _calls);
        if (During is not null) await During(reservation);
        return results[Math.Min(call, results.Length) - 1];
    }
}

internal sealed class FakeEventPublisher : IReservationEventPublisher
{
    public List<(ReservationEventType Type, Guid ReservationId)> Published { get; } = [];

    public bool Fail { get; set; }

    public Task PublishAsync(ReservationEventType type, Reservation reservation, CancellationToken ct)
    {
        if (Fail) throw new InvalidOperationException("Notifications caído");
        lock (Published) Published.Add((type, reservation.Id));
        return Task.CompletedTask;
    }
}

internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan time) => Now += time;
}
