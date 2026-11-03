using Booking.Data.Entities;
using Booking.Data.Persistence;
using Xunit;

namespace Booking.Data.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class ReservationRepositoryOccupiedSeatsTests
{
    private readonly PostgresFixture _fixture;

    public ReservationRepositoryOccupiedSeatsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task SeedAsync(params Reservation[] reservations)
    {
        await using var ctx = _fixture.CreateContext();
        foreach (var r in reservations)
        {
            ctx.Add(r);
        }
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task EventoSinReservas_DevuelveListaVacia()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);

        var occupiedSeats = await repository.GetOccupiedSeatIdsAsync(eventId, CancellationToken.None);

        Assert.Empty(occupiedSeats);
    }

    [Fact]
    public async Task HeldYConfirmedDelEvento_AparecentSusAsientos()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId1 = Guid.NewGuid();
        var seatId2 = Guid.NewGuid();

        var held = TestData.BuildHeldReservation(eventId: eventId, seatId: seatId1);
        var confirmed = new Reservation
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            EventId = eventId,
            SeatId = seatId2,
            Status = ReservationStatus.Confirmed,
            ExpiresAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await SeedAsync(held, confirmed);

        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);
        var occupiedSeats = await repository.GetOccupiedSeatIdsAsync(eventId, CancellationToken.None);

        Assert.Equal(2, occupiedSeats.Count);
        Assert.Contains(seatId1, occupiedSeats);
        Assert.Contains(seatId2, occupiedSeats);
    }

    [Fact]
    public async Task RejectedYExpiredDelEvento_NoAparecen()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId1 = Guid.NewGuid();
        var seatId2 = Guid.NewGuid();

        var rejected = TestData.BuildRejectedReservation(eventId: eventId, seatId: seatId1);
        var expired = new Reservation
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            EventId = eventId,
            SeatId = seatId2,
            Status = ReservationStatus.Expired,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        };

        await SeedAsync(rejected, expired);

        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);
        var occupiedSeats = await repository.GetOccupiedSeatIdsAsync(eventId, CancellationToken.None);

        Assert.Empty(occupiedSeats);
    }

    [Fact]
    public async Task ReservasDeOtroEvento_NoAparecen()
    {
        await _fixture.ResetAsync();

        var eventId1 = Guid.NewGuid();
        var eventId2 = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        var held = TestData.BuildHeldReservation(eventId: eventId1, seatId: seatId);
        var otherEvent = TestData.BuildHeldReservation(eventId: eventId2, seatId: Guid.NewGuid());

        await SeedAsync(held, otherEvent);

        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);
        var occupiedSeats = await repository.GetOccupiedSeatIdsAsync(eventId1, CancellationToken.None);

        Assert.Single(occupiedSeats);
        Assert.Equal(seatId, occupiedSeats[0]);
    }

    [Fact]
    public async Task UnAsientoConRejectedYHeld_ApareceUnaSolaVez()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        var rejected = TestData.BuildRejectedReservation(eventId: eventId, seatId: seatId);
        var held = TestData.BuildHeldReservation(eventId: eventId, seatId: seatId);

        await SeedAsync(rejected, held);

        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);
        var occupiedSeats = await repository.GetOccupiedSeatIdsAsync(eventId, CancellationToken.None);

        Assert.Single(occupiedSeats);
        Assert.Equal(seatId, occupiedSeats[0]);
    }

    [Fact]
    public async Task HeldVencidaSinProcesar_SigueApareciendo()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var expiredHeld = TestData.BuildHeldReservation(
            eventId: eventId,
            seatId: seatId,
            expiresAt: now.AddSeconds(-10));

        await SeedAsync(expiredHeld);

        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);
        var occupiedSeats = await repository.GetOccupiedSeatIdsAsync(eventId, CancellationToken.None);

        // Aunque venció, no fue procesada por ExpireHeldAsync, así que sigue apareciendo.
        Assert.Single(occupiedSeats);
        Assert.Equal(seatId, occupiedSeats[0]);
    }
}
