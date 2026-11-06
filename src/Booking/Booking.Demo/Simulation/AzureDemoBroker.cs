using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Booking.Api.Messaging;
using Booking.Business.Messages;
using Booking.Data.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Demo.Simulation;

// Modo --azure: hace de Availability publicando en el topic REAL de Azure Service Bus.
// Quien consume es el ReservationRequestedConsumer de Booking.Api (el código real),
// registrado con AddReservationRequestConsumer en Program.cs.
//
// Para saber cuándo terminó, mira el repositorio en memoria: una solicitud está lista
// cuando su reserva llegó a un estado final (Confirmed, Rejected o Expired).
public sealed class AzureDemoBroker(
    ServiceBusClient client,
    IOptions<BrokerOptions> options,
    InMemoryReservationRepository repository,
    ILoggerFactory loggers) : IDemoBroker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusSender _sender = client.CreateSender(options.Value.Topic);
    private readonly ILogger _logger = loggers.CreateLogger("Availability");
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    public string Description =>
        $"Azure Service Bus ({options.Value.Topic} / suscripción {options.Value.Subscription})";

    public string DeadLetterInfo => "ver la dead-letter queue de la suscripción en el portal de Azure";

    public async Task PublishAsync(ReservationRequestedMessage message)
    {
        _pending[message.RequestId] = 0;

        var serviceBusMessage = new ServiceBusMessage(BinaryData.FromObjectAsJson(message, Json))
        {
            // En el simulador el MessageId es aleatorio a propósito: si el topic tiene
            // detección de duplicados, con MessageId = RequestId el escenario "mensaje
            // duplicado" no llegaría a Booking y no se podría ver la idempotencia.
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = message.RequestId.ToString(),
            ContentType = "application/json",
            Subject = "ReservationRequested",
        };

        await _sender.SendMessageAsync(serviceBusMessage);
        _logger.LogInformation("Publicó la solicitud {Request} en {Topic}",
            message.RequestId.ToString()[..8], options.Value.Topic);
    }

    public async Task WaitUntilIdleAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!_pending.IsEmpty && DateTime.UtcNow < until)
        {
            var rows = repository.Snapshot();
            foreach (var requestId in _pending.Keys)
            {
                if (rows.Any(r => r.RequestId == requestId && r.Status != ReservationStatus.Held))
                    _pending.TryRemove(requestId, out _);
            }
            await Task.Delay(300);
        }

        if (!_pending.IsEmpty)
            _logger.LogWarning("{Count} solicitud(es) siguen sin resultado final después de {Seconds}s",
                _pending.Count, timeout.TotalSeconds);
        _pending.Clear();
    }
}
