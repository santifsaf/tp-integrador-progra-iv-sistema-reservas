using Booking.Data.Entities;
using Booking.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Booking.Data.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class ReservationRepositoryResolveTests
{
    private readonly PostgresFixture _fixture;
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    public ReservationRepositoryResolveTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HeldVigenteAConfirmed_DevuelveTrueYActualiza()
    {
        await _fixture.ResetAsync();

        var held = TestData.BuildHeldReservation(expiresAt: FixedNow.AddMinutes(1));
        await using var ctx = _fixture.CreateContext();
        ctx.Add(held);
        await ctx.SaveChangesAsync();

        var repository = new ReservationRepository(ctx);
        var result = await repository.TryResolveHeldAsync(
            held.Id,
            ReservationStatus.Confirmed,
            null,
            FixedNow,
            CancellationToken.None);

        Assert.True(result);

        await using var verifyCtx = _fixture.CreateContext();
        var persisted = await verifyCtx.Reservations.FirstAsync(r => r.Id == held.Id);
        Assert.Equal(ReservationStatus.Confirmed, persisted.Status);
        Assert.Null(persisted.RejectionReason);
        Assert.Equal(FixedNow, persisted.UpdatedAt);
    }

    [Fact]
    public async Task HeldVigenteARejectedConMotivo_DevuelveTrueYPersiste()
    {
        await _fixture.ResetAsync();

        var held = TestData.BuildHeldReservation(expiresAt: FixedNow.AddMinutes(1));
        await using var ctx = _fixture.CreateContext();
        ctx.Add(held);
        await ctx.SaveChangesAsync();

        var repository = new ReservationRepository(ctx);
        var result = await repository.TryResolveHeldAsync(
            held.Id,
            ReservationStatus.Rejected,
            RejectionReason.PaymentFailed,
            FixedNow,
            CancellationToken.None);

        Assert.True(result);

        await using var verifyCtx = _fixture.CreateContext();
        var persisted = await verifyCtx.Reservations.FirstAsync(r => r.Id == held.Id);
        Assert.Equal(ReservationStatus.Rejected, persisted.Status);
        Assert.Equal(RejectionReason.PaymentFailed, persisted.RejectionReason);
        Assert.Equal(FixedNow, persisted.UpdatedAt);
    }

    [Fact]
    public async Task HeldConExpiresAtAnteriorANow_DevuelveFalse()
    {
        await _fixture.ResetAsync();

        var held = TestData.BuildHeldReservation(expiresAt: FixedNow.AddSeconds(-1));
        await using var ctx = _fixture.CreateContext();
        ctx.Add(held);
        await ctx.SaveChangesAsync();

        var repository = new ReservationRepository(ctx);
        var result = await repository.TryResolveHeldAsync(
            held.Id,
            ReservationStatus.Confirmed,
            null,
            FixedNow,
            CancellationToken.None);

        Assert.False(result);

        await using var verifyCtx = _fixture.CreateContext();
        var persisted = await verifyCtx.Reservations.FirstAsync(r => r.Id == held.Id);
        Assert.Equal(ReservationStatus.Held, persisted.Status);
    }

    [Fact]
    public async Task HeldConExpiresAtIgualANow_DevuelveFalse()
    {
        await _fixture.ResetAsync();

        var held = TestData.BuildHeldReservation(expiresAt: FixedNow);
        await using var ctx = _fixture.CreateContext();
        ctx.Add(held);
        await ctx.SaveChangesAsync();

        var repository = new ReservationRepository(ctx);
        var result = await repository.TryResolveHeldAsync(
            held.Id,
            ReservationStatus.Confirmed,
            null,
            FixedNow,
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ReservaYaConfirmed_ResolverlaDenuevoDevuelveFalse()
    {
        await _fixture.ResetAsync();

        var confirmed = new Reservation
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            SeatId = Guid.NewGuid(),
            Status = ReservationStatus.Confirmed,
            ExpiresAt = null,
            CreatedAt = FixedNow.AddMinutes(-10),
            UpdatedAt = FixedNow.AddMinutes(-5),
        };

        await using var ctx = _fixture.CreateContext();
        ctx.Add(confirmed);
        await ctx.SaveChangesAsync();

        var repository = new ReservationRepository(ctx);
        var result = await repository.TryResolveHeldAsync(
            confirmed.Id,
            ReservationStatus.Rejected,
            RejectionReason.PaymentFailed,
            FixedNow,
            CancellationToken.None);

        Assert.False(result);

        await using var verifyCtx = _fixture.CreateContext();
        var persisted = await verifyCtx.Reservations.FirstAsync(r => r.Id == confirmed.Id);
        Assert.Equal(ReservationStatus.Confirmed, persisted.Status);
        Assert.Null(persisted.RejectionReason);
    }

    [Fact]
    public async Task IdInexistente_DevuelveFalse()
    {
        await _fixture.ResetAsync();

        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);

        var result = await repository.TryResolveHeldAsync(
            Guid.NewGuid(),
            ReservationStatus.Confirmed,
            null,
            FixedNow,
            CancellationToken.None);

        Assert.False(result);
    }

    [Theory]
    [InlineData(ReservationStatus.Held)]
    [InlineData(ReservationStatus.Expired)]
    public async Task DestinoInvalido_LanzaArgumentOutOfRangeException(ReservationStatus invalidStatus)
    {
        await _fixture.ResetAsync();

        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repository.TryResolveHeldAsync(
                Guid.NewGuid(),
                invalidStatus,
                null,
                FixedNow,
                CancellationToken.None));

        Assert.Equal("to", ex.ParamName);
    }

    [Fact]
    public async Task RejectedSinMotivo_LanzaArgumentException()
    {
        await _fixture.ResetAsync();

        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => repository.TryResolveHeldAsync(
                Guid.NewGuid(),
                ReservationStatus.Rejected,
                null,
                FixedNow,
                CancellationToken.None));

        Assert.Equal("reason", ex.ParamName);
    }

    [Fact]
    public async Task Carrera_DiezTareasEnParalelo_SoloUnaLograActualizar()
    {
        await _fixture.ResetAsync();

        var held = TestData.BuildHeldReservation(expiresAt: FixedNow.AddMinutes(1));
        await using var ctx = _fixture.CreateContext();
        ctx.Add(held);
        await ctx.SaveChangesAsync();

        using var gate = new System.Threading.CountdownEvent(10);

        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            gate.Signal();
            gate.Wait();

            await using var taskCtx = _fixture.CreateContext();
            var repo = new ReservationRepository(taskCtx);
            var to = i % 2 == 0 ? ReservationStatus.Confirmed : ReservationStatus.Rejected;
            RejectionReason? reason = i % 2 == 0 ? null : RejectionReason.PaymentUnavailable;
            return await repo.TryResolveHeldAsync(held.Id, to, reason, FixedNow, CancellationToken.None);
        })).ToList();

        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r);
        Assert.Equal(9, results.Count(r => !r));

        await using var verifyCtx = _fixture.CreateContext();
        var persisted = await verifyCtx.Reservations.FirstAsync(r => r.Id == held.Id);
        Assert.True(persisted.Status is ReservationStatus.Confirmed or ReservationStatus.Rejected);
    }

    [Fact]
    public async Task PasarARejected_LiberaElAsiento()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        var held = TestData.BuildHeldReservation(
            eventId: eventId,
            seatId: seatId,
            expiresAt: FixedNow.AddMinutes(1));

        await using var ctx = _fixture.CreateContext();
        ctx.Add(held);
        await ctx.SaveChangesAsync();

        var repository = new ReservationRepository(ctx);

        var result = await repository.TryResolveHeldAsync(
            held.Id,
            ReservationStatus.Rejected,
            RejectionReason.PaymentFailed,
            FixedNow,
            CancellationToken.None);

        Assert.True(result);

        var second = TestData.BuildHeldReservation(eventId: eventId, seatId: seatId);
        await using var ctx2 = _fixture.CreateContext();
        var holdResult = await new ReservationRepository(ctx2).TryHoldAsync(second, CancellationToken.None);

        Assert.Equal(HoldResult.Created, holdResult);
    }

    [Fact]
    public async Task PasarAConfirmed_MantieneLaOcupacion()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        var held = TestData.BuildHeldReservation(
            eventId: eventId,
            seatId: seatId,
            expiresAt: FixedNow.AddMinutes(1));

        await using var ctx = _fixture.CreateContext();
        ctx.Add(held);
        await ctx.SaveChangesAsync();

        var repository = new ReservationRepository(ctx);

        var result = await repository.TryResolveHeldAsync(
            held.Id,
            ReservationStatus.Confirmed,
            null,
            FixedNow,
            CancellationToken.None);

        Assert.True(result);

        var second = TestData.BuildHeldReservation(eventId: eventId, seatId: seatId);
        await using var ctx2 = _fixture.CreateContext();
        var holdResult = await new ReservationRepository(ctx2).TryHoldAsync(second, CancellationToken.None);

        Assert.Equal(HoldResult.SeatOccupied, holdResult);
    }
}
