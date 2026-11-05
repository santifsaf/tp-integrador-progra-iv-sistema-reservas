using Booking.Data.Entities;
using Booking.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Booking.Data.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class ReservationRepositoryRejectedTests
{
    private readonly PostgresFixture _fixture;

    public ReservationRepositoryRejectedTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<HoldResult> ExecuteTryHoldAsync(Reservation reservation)
    {
        await using var context = _fixture.CreateContext();
        var repository = new ReservationRepository(context);
        return await repository.TryHoldAsync(reservation, CancellationToken.None);
    }

    private async Task<bool> ExecuteAddRejectedAsync(Reservation reservation, BookingDbContext context)
    {
        var repository = new ReservationRepository(context);
        return await repository.AddRejectedAsync(reservation, CancellationToken.None);
    }

    [Fact]
    public async Task TrasUnSeatOccupied_MutarARejectedEnMismoContexto_DevuelveTrueYPersiste()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var first = TestData.BuildHeldReservation(eventId: eventId, seatId: seatId);

        await ExecuteTryHoldAsync(first);

        await using var contextReused = _fixture.CreateContext();
        var repositoryReused = new ReservationRepository(contextReused);

        var second = TestData.BuildHeldReservation(eventId: eventId, seatId: seatId);
        var holdResult = await repositoryReused.TryHoldAsync(second, CancellationToken.None);
        Assert.Equal(HoldResult.SeatOccupied, holdResult);

        second.Status = ReservationStatus.Rejected;
        second.RejectionReason = RejectionReason.SeatOccupied;

        var addResult = await repositoryReused.AddRejectedAsync(second, CancellationToken.None);
        Assert.True(addResult);

        await using var verifyContext = _fixture.CreateContext();
        var verifyRepository = new ReservationRepository(verifyContext);
        var persisted = await verifyRepository.GetByRequestIdAsync(second.RequestId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(ReservationStatus.Rejected, persisted.Status);
        Assert.Equal(RejectionReason.SeatOccupied, persisted.RejectionReason);
    }

    [Fact]
    public async Task DosVecesConMismoRequestId_LaSegundaDevuelveFalse()
    {
        await _fixture.ResetAsync();

        var requestId = Guid.NewGuid();
        var first = TestData.BuildRejectedReservation(requestId: requestId, reason: RejectionReason.PaymentFailed);

        await using var ctx1 = _fixture.CreateContext();
        ctx1.Add(first);
        await ctx1.SaveChangesAsync();

        var second = TestData.BuildRejectedReservation(requestId: requestId, reason: RejectionReason.PaymentFailed);
        await using var ctx2 = _fixture.CreateContext();
        var result = await ExecuteAddRejectedAsync(second, ctx2);

        Assert.False(result);

        Assert.Equal(1, await ctx2.Reservations.CountAsync(r => r.RequestId == requestId));
        Assert.Empty(ctx2.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UnaReservaRejectedNoLiberaElAsiento()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        var held = TestData.BuildHeldReservation(eventId: eventId, seatId: seatId);
        await ExecuteTryHoldAsync(held);

        var rejected = TestData.BuildRejectedReservation(eventId: eventId, seatId: seatId);
        await using var ctxRejected = _fixture.CreateContext();
        var addResult = await ExecuteAddRejectedAsync(rejected, ctxRejected);
        Assert.True(addResult);

        var third = TestData.BuildHeldReservation(eventId: eventId, seatId: seatId);
        var holdResult = await ExecuteTryHoldAsync(third);

        Assert.Equal(HoldResult.SeatOccupied, holdResult);
    }

    [Fact]
    public async Task StatusDistintoDeRejected_LanzaArgumentException()
    {
        await _fixture.ResetAsync();

        var held = TestData.BuildHeldReservation();

        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => repository.AddRejectedAsync(held, CancellationToken.None));

        Assert.Equal("reservation", ex.ParamName);
    }

    [Fact]
    public async Task AddRejectedAsync_Exitoso_NoDejaLaEntidadTrackeada()
    {
        await _fixture.ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);

        var added = await repository.AddRejectedAsync(TestData.BuildRejectedReservation(), CancellationToken.None);

        Assert.True(added);
        Assert.Empty(ctx.ChangeTracker.Entries());
    }

    [Fact]
    public async Task AddRejectedAsync_Exitoso_UnaMutacionEnMemoriaNoSePersisteConUnGuardadoPosterior()
    {
        await _fixture.ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var repository = new ReservationRepository(ctx);

        var first = TestData.BuildRejectedReservation(reason: RejectionReason.SeatOccupied);
        Assert.True(await repository.AddRejectedAsync(first, CancellationToken.None));

        // La capa de negocio cambia el objeto en memoria...
        first.RejectionReason = RejectionReason.PaymentFailed;

        // ...y un guardado posterior en el mismo contexto no debe arrastrar ese cambio.
        Assert.True(await repository.AddRejectedAsync(TestData.BuildRejectedReservation(), CancellationToken.None));

        await using var verifyContext = _fixture.CreateContext();
        var persisted = await new ReservationRepository(verifyContext)
            .GetByRequestIdAsync(first.RequestId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(RejectionReason.SeatOccupied, persisted.RejectionReason);
    }
}
