using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Booking.Business.Messages;
using Booking.Business.Payments;
using Booking.Business.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Booking.Api.Messaging;

// Punto de entrada de Booking desde el broker (paso 4 del flujo).
//
// DECISIÓN DE ARQUITECTURA (N-Layer): igual que los controllers, el consumidor es parte
// de la capa de presentación (Booking.Api): es otra forma de "entrada" al servicio,
// por mensaje en vez de por HTTP. Solo traduce el mensaje a una llamada a la capa de
// negocio (ReservationRequestProcessor) y no tiene reglas propias. Si se cambia de
// broker, se reemplaza esta carpeta y la lógica de negocio no se toca.
//
// DECISIÓN: varias réplicas de Booking leen la MISMA suscripción ("booking"). Service Bus
// le entrega cada mensaje a una sola réplica (patrón Competing Consumers), que es lo que
// pide la consigna: "una de las réplicas de Booking toma el mensaje".
//
// DECISIÓN: se usa ServiceBusProcessor en vez de ReceiveMessageAsync (el ejemplo de la
// cátedra). El processor mantiene el loop de recepción, renueva el lock mientras el
// mensaje se procesa y permite procesar varios mensajes en paralelo.
internal sealed class ReservationRequestedConsumer(
    ServiceBusClient client,
    IServiceScopeFactory scopes,
    IOptions<BrokerOptions> options,
    ILogger<ReservationRequestedConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BrokerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateProcessor(
            _options.Topic,
            _options.Subscription,
            new ServiceBusProcessorOptions
            {
                // DECISIÓN: completamos el mensaje a mano y solo DESPUÉS de guardar en la
                // base. Si se completara antes y la réplica se cae, la solicitud se pierde.
                AutoCompleteMessages = false,
                MaxConcurrentCalls = _options.MaxConcurrentCalls,
            });

        processor.ProcessMessageAsync += HandleMessageAsync;
        processor.ProcessErrorAsync += HandleErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation(
            "Escuchando solicitudes de reserva en {Topic}/{Subscription}",
            _options.Topic, _options.Subscription);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Apagado normal de la aplicación.
        }

        await processor.StopProcessingAsync(CancellationToken.None);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;

        ReservationRequestedMessage? request;
        try
        {
            request = message.Body.ToObjectFromJson<ReservationRequestedMessage>(Json);
        }
        catch (JsonException ex)
        {
            request = null;
            logger.LogWarning(ex, "Mensaje {MessageId} con JSON inválido", message.MessageId);
        }

        // DECISIÓN: un mensaje mal formado no se va a arreglar reintentando, así que va
        // directo a la dead-letter queue (cola de mensajes muertos) para revisarlo a mano.
        if (request is null || request.RequestId == Guid.Empty)
        {
            await args.DeadLetterMessageAsync(
                message,
                deadLetterReason: "InvalidMessage",
                deadLetterErrorDescription: "El cuerpo no respeta el contrato ReservationRequestedMessage",
                cancellationToken: args.CancellationToken);
            return;
        }

        try
        {
            // Un scope por mensaje: el repositorio (DbContext) es scoped y no se comparte
            // entre mensajes que se procesan en paralelo.
            await using var scope = scopes.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<ReservationRequestProcessor>();

            await processor.ProcessAsync(request, args.CancellationToken);

            await args.CompleteMessageAsync(message, args.CancellationToken);
        }
        catch (PaymentResultUncertainException ex)
        {
            // Payment no dio un resultado claro: la reserva queda Held y se reintenta.
            // No es un error del sistema, por eso va como warning.
            logger.LogWarning(
                "{Detail} Solicitud {RequestId}, intento {Attempt}",
                ex.Message, request.RequestId, message.DeliveryCount);

            await args.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // DECISIÓN: ante un error transitorio (base caída, timeout) se abandona el
            // mensaje y Service Bus lo vuelve a entregar. Al llegar a MaxDeliveryCount
            // (10 por defecto) pasa solo a la dead-letter queue. El reintento es seguro
            // porque el procesamiento es idempotente.
            logger.LogError(ex,
                "Error procesando la solicitud {RequestId} (intento {Attempt}); se reintenta",
                request.RequestId, message.DeliveryCount);

            await args.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception,
            "Error del broker ({Source}) en {EntityPath}", args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }
}
