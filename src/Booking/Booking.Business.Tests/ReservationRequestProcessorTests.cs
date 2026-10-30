using Booking.Business.Events;
using Booking.Business.Messages;
using Booking.Business.Payments;
using Booking.Business.Services;
using Booking.Business.Tests.Fakes;
using Booking.Data.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Booking.Business.Tests;

public class ReservationRequestProcessorTests
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(2);
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid SeatId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryReservationRepository _repository = new();
    private readonly FakeEventPublisher _publisher = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

    private ReservationRequestProcessor CreateProcessor(IPaymentClient payment) => new(
        _repository,
        payment,
        _publisher,
        Options.Create(new ReservationsOptions { HoldDuration = HoldDuration }),
        _clock,
        NullLogger<ReservationRequestProcessor>.Instance);

    private ReservationExpirationService CreateExpirationService() =>
        new(_repository, _clock, NullLogger<ReservationExpirationService>.Instance);

    private static ReservationRequestedMessage NewRequest() =>
        new(Guid.NewGuid(), EventId, SeatId, UserId, DateTimeOffset.UtcNow);

    private Reservation SeedHeld(ReservationRequestedMessage request)
    {
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            RequestId = request.RequestId,
            UserId = request.UserId,
            EventId = request.EventId,
            SeatId = request.SeatId,
            Status = ReservationStatus.Held,
            ExpiresAt = _clock.Now + HoldDuration,
            CreatedAt = _clock.Now,
            UpdatedAt = _clock.Now,
        };
        _repository.Add(reservation);
        return reservation;
    }

    // ---------- Happy path ----------

    [Fact]
    public async Task FreeSeat_PaymentSucceeded_IsConfirmed()
    {
        var payment = new FakePaymentClient(PaymentResult.Succeeded);

        var reservation = await CreateProcessor(payment).ProcessAsync(NewRequest(), CancellationToken.None);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Null(reservation.RejectionReason);
        Assert.Equal(ReservationStatus.Confirmed, Assert.Single(_repository.All).Status);
        Assert.Equal(1, payment.Calls);
    }

    [Fact]
    public async Task FreeSeat_PaymentSucceeded_PublishesCreatedAndConfirmed()
    {
        var reservation = await CreateProcessor(new FakePaymentClient(PaymentResult.Succeeded))
            .ProcessAsync(NewRequest(), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                (ReservationEventType.ReservationCreated, reservation.Id),
                (ReservationEventType.ReservationConfirmed, reservation.Id),
            },
            _publisher.Published);
    }

    // ---------- Rechazos ----------

    [Fact]
    public async Task OccupiedSeat_IsRejectedWithoutCharging()
    {
        await CreateProcessor(new FakePaymentClient(PaymentResult.Succeeded)).ProcessAsync(NewRequest(), CancellationToken.None);
        var payment = new FakePaymentClient(PaymentResult.Succeeded);

        var second = await CreateProcessor(payment).ProcessAsync(NewRequest(), CancellationToken.None);

        Assert.Equal(ReservationStatus.Rejected, second.Status);
        Assert.Equal(RejectionReason.SeatOccupied, second.RejectionReason);
        Assert.Equal(0, payment.Calls);
        // Queda registrada para que el usuario pueda consultar el motivo.
        Assert.Equal(2, _repository.All.Count);
    }

    [Theory]
    [InlineData(PaymentResult.Failed, RejectionReason.PaymentFailed)]
    [InlineData(PaymentResult.Unavailable, RejectionReason.PaymentUnavailable)]
    public async Task PaymentNotSucceeded_IsRejectedWithTheRightReason(PaymentResult result, RejectionReason reason)
    {
        var reservation = await CreateProcessor(new FakePaymentClient(result)).ProcessAsync(NewRequest(), CancellationToken.None);

        Assert.Equal(ReservationStatus.Rejected, reservation.Status);
        Assert.Equal(reason, reservation.RejectionReason);
        Assert.DoesNotContain(_publisher.Published, e => e.Type == ReservationEventType.ReservationConfirmed);
    }

    [Fact]
    public async Task PaymentUnavailable_ReleasesTheSeat()
    {
        await CreateProcessor(new FakePaymentClient(PaymentResult.Unavailable)).ProcessAsync(NewRequest(), CancellationToken.None);

        var other = await CreateProcessor(new FakePaymentClient(PaymentResult.Succeeded)).ProcessAsync(NewRequest(), CancellationToken.None);

        Assert.Equal(ReservationStatus.Confirmed, other.Status);
    }

    // ---------- Pago incierto (timeout) ----------

    [Fact]
    public async Task PaymentUncertain_ThrowsForRetryAndKeepsTheSeatHeld()
    {
        var processor = CreateProcessor(new FakePaymentClient(PaymentResult.Uncertain));

        await Assert.ThrowsAsync<PaymentResultUncertainException>(
            () => processor.ProcessAsync(NewRequest(), CancellationToken.None));

        // No se rechaza: Payment pudo haber cobrado. El asiento sigue retenido.
        Assert.Equal(ReservationStatus.Held, Assert.Single(_repository.All).Status);
    }

    [Fact]
    public async Task PaymentUncertain_ThenRetrySucceeds_IsConfirmedWithTheSameReservation()
    {
        var payment = new FakePaymentClient(PaymentResult.Uncertain, PaymentResult.Succeeded);
        var processor = CreateProcessor(payment);
        var request = NewRequest();

        await Assert.ThrowsAsync<PaymentResultUncertainException>(
            () => processor.ProcessAsync(request, CancellationToken.None));
        var reservation = await processor.ProcessAsync(request, CancellationToken.None); // reentrega del broker

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Single(_repository.All);
        Assert.Equal(2, payment.Calls); // Payment es idempotente por ReservationId: no cobra dos veces
    }

    [Fact]
    public async Task PaymentUncertainUntilTheHoldExpires_EndsExpiredAndReleasesTheSeat()
    {
        var processor = CreateProcessor(new FakePaymentClient(PaymentResult.Uncertain));

        await Assert.ThrowsAsync<PaymentResultUncertainException>(
            () => processor.ProcessAsync(NewRequest(), CancellationToken.None));
        _clock.Advance(HoldDuration + TimeSpan.FromSeconds(1));
        await CreateExpirationService().ExpireHeldAsync(CancellationToken.None);

        Assert.Equal(ReservationStatus.Expired, Assert.Single(_repository.All).Status);

        var other = await CreateProcessor(new FakePaymentClient(PaymentResult.Succeeded)).ProcessAsync(NewRequest(), CancellationToken.None);
        Assert.Equal(ReservationStatus.Confirmed, other.Status);
    }

    // ---------- Idempotencia ----------

    [Fact]
    public async Task RepeatedMessage_DoesNotChargeTwiceNorCreateAnotherReservation()
    {
        var payment = new FakePaymentClient(PaymentResult.Succeeded);
        var processor = CreateProcessor(payment);
        var request = NewRequest();

        var first = await processor.ProcessAsync(request, CancellationToken.None);
        var second = await processor.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(ReservationStatus.Confirmed, second.Status);
        Assert.Single(_repository.All);
        Assert.Equal(1, payment.Calls);
    }

    [Fact]
    public async Task RepeatedMessage_WithReservationStillHeld_ResumesTheCharge()
    {
        // Simula una réplica que retuvo el asiento y se cayó antes de cobrar.
        var request = NewRequest();
        SeedHeld(request);
        var payment = new FakePaymentClient(PaymentResult.Succeeded);

        var reservation = await CreateProcessor(payment).ProcessAsync(request, CancellationToken.None);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(1, payment.Calls);
        Assert.Single(_repository.All);
    }

    [Fact]
    public async Task SameRequestBeingProcessedByAnotherReplica_DoesNotCallPayment()
    {
        // Otra réplica ya insertó la reserva de esta solicitud y la está cobrando.
        // Esta réplica no la vio en la primera búsqueda y choca al insertar.
        var request = NewRequest();
        SeedHeld(request);
        _repository.HideOnFirstLookup = request.RequestId;
        var payment = new FakePaymentClient(PaymentResult.Succeeded);

        var reservation = await CreateProcessor(payment).ProcessAsync(request, CancellationToken.None);

        Assert.Equal(0, payment.Calls);
        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Single(_repository.All);
    }

    // ---------- Concurrencia ----------

    [Fact]
    public async Task ManySimultaneousRequestsForTheSameSeat_OnlyOneGetsIt()
    {
        var payment = new FakePaymentClient(PaymentResult.Succeeded)
        {
            During = _ => Task.Delay(20),
        };
        var processor = CreateProcessor(payment);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
                processor.ProcessAsync(NewRequest(), CancellationToken.None))));

        Assert.Single(results, r => r.Status == ReservationStatus.Confirmed);
        Assert.Equal(19, results.Count(r => r.RejectionReason == RejectionReason.SeatOccupied));
        Assert.Equal(1, payment.Calls);
    }

    // ---------- Vencimiento ----------

    [Fact]
    public async Task Expiration_HeldWithoutResult_BecomesExpiredAndReleasesTheSeat()
    {
        SeedHeld(NewRequest());

        _clock.Advance(HoldDuration + TimeSpan.FromSeconds(1));
        var expired = await CreateExpirationService().ExpireHeldAsync(CancellationToken.None);

        Assert.Equal(1, expired);
        Assert.Equal(ReservationStatus.Expired, Assert.Single(_repository.All).Status);

        var next = await CreateProcessor(new FakePaymentClient(PaymentResult.Succeeded)).ProcessAsync(NewRequest(), CancellationToken.None);
        Assert.Equal(ReservationStatus.Confirmed, next.Status);
    }

    [Fact]
    public async Task Expiration_DoesNotTouchHeldWithinTheirTime()
    {
        var payment = new FakePaymentClient(PaymentResult.Succeeded)
        {
            // Mientras se espera a Payment corre el job, pero la retención todavía no venció.
            During = async _ => Assert.Equal(0, await CreateExpirationService().ExpireHeldAsync(CancellationToken.None)),
        };

        var reservation = await CreateProcessor(payment).ProcessAsync(NewRequest(), CancellationToken.None);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
    }

    [Fact]
    public async Task PaymentSucceededAfterExpiration_IsNotConfirmed()
    {
        var payment = new FakePaymentClient(PaymentResult.Succeeded)
        {
            // Payment tarda más que la retención y el job vence la reserva en el medio.
            During = async _ =>
            {
                _clock.Advance(HoldDuration + TimeSpan.FromSeconds(1));
                await CreateExpirationService().ExpireHeldAsync(CancellationToken.None);
            },
        };

        var reservation = await CreateProcessor(payment).ProcessAsync(NewRequest(), CancellationToken.None);

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.Equal(ReservationStatus.Expired, Assert.Single(_repository.All).Status);
        Assert.DoesNotContain(_publisher.Published, e => e.Type == ReservationEventType.ReservationConfirmed);
    }

    [Fact]
    public async Task RetriedMessageWithHoldAlreadyExpired_DoesNotCharge()
    {
        var request = NewRequest();
        SeedHeld(request);
        _clock.Advance(HoldDuration + TimeSpan.FromSeconds(1));
        var payment = new FakePaymentClient(PaymentResult.Succeeded);

        await CreateProcessor(payment).ProcessAsync(request, CancellationToken.None);

        Assert.Equal(0, payment.Calls);
    }

    // ---------- Notificaciones ----------

    [Fact]
    public async Task IfTheEventPublisherFails_TheReservationIsStillConfirmed()
    {
        _publisher.Fail = true;

        var reservation = await CreateProcessor(new FakePaymentClient(PaymentResult.Succeeded)).ProcessAsync(NewRequest(), CancellationToken.None);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
    }
}
