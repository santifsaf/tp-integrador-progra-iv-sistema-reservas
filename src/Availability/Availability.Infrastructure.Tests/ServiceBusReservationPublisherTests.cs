using System.Text.Json;
using Availability.Application.Abstractions;
using Availability.Domain.Entities;
using Availability.Domain.Enums;
using Availability.Infrastructure.Messaging;
using Azure.Messaging.ServiceBus;
using Booking.Business.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Availability.Infrastructure.Tests;

public sealed class ServiceBusReservationPublisherTests
{
    [Fact]
    public async Task PublishAsync_SendsExpectedContractAndCorrelationProperties()
    {
        var sender = new CapturingServiceBusSender();
        var eventId = Guid.NewGuid();
        var request = new ReservationRequest
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            EventId = eventId,
            SeatId = Guid.NewGuid(),
            Status = RequestStatus.Received,
            CreatedAt = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc)
        };
        var events = new StubEventRepository(new Event
        {
            Id = eventId,
            Name = "Test event",
            StartsAt = new DateTime(2026, 12, 1, 20, 0, 0, DateTimeKind.Utc),
            PriceCents = 15000,
            Currency = "ARS"
        });
        var publisher = new ServiceBusReservationPublisher(
            sender,
            events,
            NullLogger<ServiceBusReservationPublisher>.Instance);

        await publisher.PublishAsync(request, CancellationToken.None);

        Assert.NotNull(sender.Message);
        Assert.Equal(request.Id.ToString(), sender.Message.MessageId);
        Assert.Equal(request.Id.ToString(), sender.Message.CorrelationId);
        Assert.Equal("application/json", sender.Message.ContentType);

        var body = JsonSerializer.Deserialize<ReservationRequestedMessage>(
            sender.Message.Body.ToString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(body);
        Assert.Equal(request.Id, body.RequestId);
        Assert.Equal(request.EventId, body.EventId);
        Assert.Equal(request.SeatId, body.SeatId);
        Assert.Equal(request.UserId, body.UserId);
        Assert.Equal(request.CreatedAt, body.RequestedAt.UtcDateTime);
        Assert.Equal(15000, body.AmountCents);
        Assert.Equal("ARS", body.Currency);
    }

    private sealed class StubEventRepository(Event eventDetails) : IEventRepository
    {
        public Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Event>>([eventDetails]);

        public Task<Event?> GetByIdAsync(Guid eventId, CancellationToken ct) =>
            Task.FromResult(eventId == eventDetails.Id ? eventDetails : null);
    }

    private sealed class CapturingServiceBusSender : ServiceBusSender
    {
        public ServiceBusMessage? Message { get; private set; }

        public override Task SendMessageAsync(
            ServiceBusMessage message,
            CancellationToken cancellationToken = default)
        {
            Message = message;
            return Task.CompletedTask;
        }
    }
}
