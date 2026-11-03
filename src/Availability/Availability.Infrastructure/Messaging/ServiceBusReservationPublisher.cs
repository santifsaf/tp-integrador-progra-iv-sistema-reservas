using System.Text.Json;
using System.Diagnostics;
using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Domain.Entities;
using Azure.Messaging.ServiceBus;
using Booking.Business.Messages;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure.Messaging;

public sealed class ServiceBusReservationPublisher(
    ServiceBusSender sender,
    IEventRepository events,
    ILogger<ServiceBusReservationPublisher> logger) : IReservationPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(ReservationRequest request, CancellationToken ct)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = request.Id
        });
        logger.LogInformation(
            "Infrastructure operation {Event} {Operation} {CorrelationId} {RequestId}",
            "Started",
            "PublishReservation",
            request.Id,
            request.Id);

        try
        {
            var eventDetails = await events.GetByIdAsync(request.EventId, ct)
                ?? throw new InvalidOperationException($"Event {request.EventId} was not found.");

            var body = new ReservationRequestedMessage(
                request.Id,
                request.EventId,
                request.SeatId,
                request.UserId,
                new DateTimeOffset(DateTime.SpecifyKind(request.CreatedAt, DateTimeKind.Utc)),
                eventDetails.PriceCents,
                eventDetails.Currency);

            var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(body, JsonOptions))
            {
                ContentType = "application/json",
                MessageId = request.Id.ToString(),
                CorrelationId = request.Id.ToString()
            };

            try
            {
                await sender.SendMessageAsync(message, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ExternalServiceUnavailableException(
                    "No se pudo publicar la solicitud en Azure Service Bus.",
                    ex);
            }

            logger.LogInformation(
                "Infrastructure operation {Event} {Operation} {CorrelationId} {RequestId} {DurationMs}",
                "Completed",
                "PublishReservation",
                request.Id,
                request.Id,
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Infrastructure operation {Event} {Operation} {CorrelationId} {RequestId} {DurationMs}",
                "Failed",
                "PublishReservation",
                request.Id,
                request.Id,
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            throw;
        }
    }
}