using System.Collections.Concurrent;
using Booking.Business.Payments;
using Booking.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Booking.Demo.Simulation;

// Cómo se va a comportar Payment para una solicitud.
public enum PaymentScenario
{
    Succeeded,              // cobra y responde éxito
    Failed,                 // responde "pago rechazado"
    Unavailable,            // responde "pago no disponible"
    TimeoutThenSucceeded,   // cobra, pero la respuesta se pierde (timeout); al reintentar devuelve el éxito guardado
    NeverResponds,          // caído o sin respuesta: siempre error de comunicación, nunca cobra
}

// Reemplaza al cliente gRPC de Santi + al servicio Payment.
//
// Es IDEMPOTENTE por ReservationId, como se acordó para el Payment real: si Booking
// vuelve a pedir el cobro de la misma reserva, devuelve el resultado guardado sin
// cobrar de nuevo. RealCharges cuenta los cobros de verdad para comprobarlo.
public sealed class SimulatedPaymentClient(ILoggerFactory loggers) : IPaymentClient
{
    private readonly ILogger _logger = loggers.CreateLogger("Payment");
    private readonly ConcurrentDictionary<Guid, PaymentScenario> _scenarioByRequest = new();
    private readonly ConcurrentDictionary<Guid, PaymentResult> _storedResultByReservation = new();
    private readonly ConcurrentDictionary<Guid, int> _realChargesByReservation = new();

    public void Plan(Guid requestId, PaymentScenario scenario) => _scenarioByRequest[requestId] = scenario;

    public int RealCharges(Guid reservationId) => _realChargesByReservation.GetValueOrDefault(reservationId);

    public async Task<PaymentResult> ChargeAsync(Reservation reservation, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(200, 600), ct); // latencia de la llamada gRPC

        // Idempotencia: ¿ya procesé un cobro para esta reserva?
        if (_storedResultByReservation.TryGetValue(reservation.Id, out var stored))
        {
            _logger.LogInformation("Reserva {Id}: cobro ya procesado antes, devuelvo {Result} SIN cobrar de nuevo",
                Short(reservation.Id), stored);
            return stored;
        }

        var scenario = _scenarioByRequest.GetValueOrDefault(reservation.RequestId, PaymentScenario.Succeeded);
        switch (scenario)
        {
            case PaymentScenario.Succeeded:
                return Charge(reservation, PaymentResult.Succeeded);

            case PaymentScenario.Failed:
                _storedResultByReservation[reservation.Id] = PaymentResult.Failed;
                _logger.LogInformation("Reserva {Id}: pago RECHAZADO", Short(reservation.Id));
                return PaymentResult.Failed;

            case PaymentScenario.Unavailable:
                _logger.LogInformation("Reserva {Id}: pago NO DISPONIBLE", Short(reservation.Id));
                return PaymentResult.Unavailable;

            case PaymentScenario.TimeoutThenSucceeded:
                Charge(reservation, PaymentResult.Succeeded);
                _logger.LogWarning("Reserva {Id}: cobró, pero la respuesta se perdió (timeout)", Short(reservation.Id));
                return PaymentResult.Uncertain;

            case PaymentScenario.NeverResponds:
                await Task.Delay(800, ct);
                _logger.LogWarning("Reserva {Id}: Payment no respondió (timeout)", Short(reservation.Id));
                return PaymentResult.Uncertain;

            default:
                throw new InvalidOperationException($"Escenario no contemplado: {scenario}");
        }
    }

    private PaymentResult Charge(Reservation reservation, PaymentResult result)
    {
        var count = _realChargesByReservation.AddOrUpdate(reservation.Id, 1, (_, n) => n + 1);
        _storedResultByReservation[reservation.Id] = result;
        _logger.LogInformation("Reserva {Id}: COBRO REALIZADO (cobros reales de esta reserva: {Count})",
            Short(reservation.Id), count);
        return result;
    }

    private static string Short(Guid id) => id.ToString()[..8];
}
