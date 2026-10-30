using System.Threading.Channels;
using Booking.Business.Messages;
using Booking.Business.Payments;
using Booking.Business.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Booking.Demo.Simulation;

// Una entrega de un mensaje. El mismo mensaje puede entregarse varias veces.
public sealed class Delivery(ReservationRequestedMessage message)
{
    public ReservationRequestedMessage Message { get; } = message;
    public int DeliveryCount { get; set; }
}

// Imita la suscripción "booking" de Azure Service Bus:
//   - cada mensaje lo recibe UNA sola réplica (competing consumers),
//   - si la réplica falla, el mensaje se "abandona" y vuelve a entregarse más tarde,
//   - después de MaxDeliveryCount intentos va a la dead-letter queue.
public sealed class InMemoryBroker : IDemoBroker
{
    public const int MaxDeliveryCount = 10;
    public static readonly TimeSpan RedeliveryDelay = TimeSpan.FromSeconds(3);

    private readonly Channel<Delivery> _channel = Channel.CreateUnbounded<Delivery>();
    private int _outstanding;

    public List<Delivery> DeadLetters { get; } = [];

    public ChannelReader<Delivery> Reader => _channel.Reader;

    public string Description =>
        $"en memoria, {BookingReplicas.ReplicaCount} réplicas de Booking, reintento cada {RedeliveryDelay.TotalSeconds:0}s";

    public string DeadLetterInfo => DeadLetters.Count.ToString();

    public Task PublishAsync(ReservationRequestedMessage message)
    {
        Interlocked.Increment(ref _outstanding);
        _channel.Writer.TryWrite(new Delivery(message));
        return Task.CompletedTask;
    }

    public void Complete(Delivery delivery) => Interlocked.Decrement(ref _outstanding);

    // Devuelve true si se va a reintentar, false si fue a la dead-letter queue.
    public bool Abandon(Delivery delivery)
    {
        if (delivery.DeliveryCount >= MaxDeliveryCount)
        {
            lock (DeadLetters) DeadLetters.Add(delivery);
            Interlocked.Decrement(ref _outstanding);
            return false;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(RedeliveryDelay);
            _channel.Writer.TryWrite(delivery);
        });
        return true;
    }

    public async Task WaitUntilIdleAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref _outstanding) > 0 && DateTime.UtcNow < until)
            await Task.Delay(100);
    }
}

// Levanta N "réplicas de Booking" que consumen del broker en paralelo. Cada réplica hace
// lo mismo que ReservationRequestedConsumer en Booking.Api: un scope por mensaje, llama a
// la capa de negocio, completa si salió bien y abandona si falló.
public sealed class BookingReplicas(
    InMemoryBroker broker,
    IServiceScopeFactory scopes,
    SeatCatalog seats,
    ILoggerFactory loggers) : BackgroundService
{
    public const int ReplicaCount = 3;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(1, ReplicaCount).Select(i => RunReplicaAsync($"Booking-{i}", stoppingToken)));

    private async Task RunReplicaAsync(string name, CancellationToken ct)
    {
        var logger = loggers.CreateLogger(name);

        try
        {
            await foreach (var delivery in broker.Reader.ReadAllAsync(ct))
            {
                delivery.DeliveryCount++;
                var request = delivery.Message;
                logger.LogInformation("Tomó la solicitud {Request} (asiento {Seat}, intento {Attempt})",
                    request.RequestId.ToString()[..8], seats.Label(request.SeatId), delivery.DeliveryCount);

                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<ReservationRequestProcessor>();
                    var reservation = await processor.ProcessAsync(request, ct);

                    broker.Complete(delivery);
                    logger.LogInformation("Completó la solicitud {Request}: {Status}{Reason}",
                        request.RequestId.ToString()[..8], reservation.Status,
                        reservation.RejectionReason is null ? "" : $" ({reservation.RejectionReason})");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var retry = broker.Abandon(delivery);
                    var what = ex is PaymentResultUncertainException ? "resultado de pago incierto" : ex.Message;
                    if (retry)
                        logger.LogWarning("Abandonó la solicitud {Request} ({What}); el broker la reentrega en {Delay}s",
                            request.RequestId.ToString()[..8], what, InMemoryBroker.RedeliveryDelay.TotalSeconds);
                    else
                        logger.LogError("Solicitud {Request} enviada a la dead-letter queue después de {Max} intentos",
                            request.RequestId.ToString()[..8], InMemoryBroker.MaxDeliveryCount);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado del simulador.
        }
    }
}
