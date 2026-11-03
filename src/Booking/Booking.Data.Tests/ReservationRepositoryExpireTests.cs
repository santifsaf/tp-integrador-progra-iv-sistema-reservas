using Booking.Data.Entities;
using Booking.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Booking.Data.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class ReservationRepositoryExpireTests
{
    private readonly PostgresFixture _fixture;
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    public ReservationRepositoryExpireTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // Se siembra con un contexto propio y descartable: así el contexto del repositorio
    // arranca sin entidades trackeadas y el test no depende del estado del tracker.
    private async Task SeedAsync(params Reservation[] reservations)
    {
        await using var seedCtx = _fixture.CreateContext();
        seedCtx.Reservations.AddRange(reservations);
        await seedCtx.SaveChangesAsync();
    }

    [Fact]
    public async Task HeldConExpiresAtAnteriorANow_SeVenceYApareceEnLista()
    {
        await _fixture.ResetAsync();

        var held = TestData.BuildHeldReservation(expiresAt: FixedNow.AddSeconds(-1));
        await SeedAsync(held);

        await using var ctx = _fixture.CreateContext();
        var expired = await new ReservationRepository(ctx).ExpireHeldAsync(FixedNow, CancellationToken.None);

        Assert.Single(expired);
        Assert.Equal(held.Id, expired[0].Id);
        Assert.Equal(ReservationStatus.Expired, expired[0].Status);
        Assert.Equal(FixedNow, expired[0].UpdatedAt);
        Assert.Null(expired[0].RejectionReason);

        // Verificar en otro contexto.
        await using var verifyCtx = _fixture.CreateContext();
        var persisted = await verifyCtx.Reservations.FirstAsync(r => r.Id == held.Id);
        Assert.Equal(ReservationStatus.Expired, persisted.Status);
        Assert.Equal(FixedNow, persisted.UpdatedAt);
        Assert.Null(persisted.RejectionReason);
    }

    [Fact]
    public async Task HeldConExpiresAtIgualANow_SeVenceElLimiteEsInclusivo()
    {
        await _fixture.ResetAsync();

        await SeedAsync(TestData.BuildHeldReservation(expiresAt: FixedNow));

        await using var ctx = _fixture.CreateContext();
        var expired = await new ReservationRepository(ctx).ExpireHeldAsync(FixedNow, CancellationToken.None);

        Assert.Single(expired);
        Assert.Equal(ReservationStatus.Expired, expired[0].Status);
    }

    [Fact]
    public async Task HeldVigente_NoSeToca()
    {
        await _fixture.ResetAsync();

        var held = TestData.BuildHeldReservation(expiresAt: FixedNow.AddSeconds(1));
        await SeedAsync(held);

        await using var ctx = _fixture.CreateContext();
        var expired = await new ReservationRepository(ctx).ExpireHeldAsync(FixedNow, CancellationToken.None);

        Assert.Empty(expired);

        // Verificar que sigue Held.
        await using var verifyCtx = _fixture.CreateContext();
        var persisted = await verifyCtx.Reservations.FirstAsync(r => r.Id == held.Id);
        Assert.Equal(ReservationStatus.Held, persisted.Status);
    }

    [Fact]
    public async Task ConfirmedYRejectedConExpiresAtPasado_NoSeTocan()
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
            ExpiresAt = FixedNow.AddSeconds(-1),
            CreatedAt = FixedNow.AddMinutes(-10),
            UpdatedAt = FixedNow.AddMinutes(-5),
        };

        // DECISIÓN: la Rejected también lleva ExpiresAt en el pasado. Con ExpiresAt nulo la
        // comparación 'expires_at <= now' nunca es verdadera y el test pasaría aunque el
        // WHERE no filtrara por estado.
        var rejected = TestData.BuildRejectedReservation(createdAt: FixedNow.AddSeconds(-1));
        rejected.ExpiresAt = FixedNow.AddSeconds(-1);

        await SeedAsync(confirmed, rejected);

        await using var ctx = _fixture.CreateContext();
        var expired = await new ReservationRepository(ctx).ExpireHeldAsync(FixedNow, CancellationToken.None);

        Assert.Empty(expired);

        // Verificar que no cambiaron (ni el estado ni la fecha de modificación).
        await using var verifyCtx = _fixture.CreateContext();

        var verifiedConfirmed = await verifyCtx.Reservations.FirstAsync(r => r.Id == confirmed.Id);
        Assert.Equal(ReservationStatus.Confirmed, verifiedConfirmed.Status);
        Assert.Equal(confirmed.UpdatedAt, verifiedConfirmed.UpdatedAt);

        var verifiedRejected = await verifyCtx.Reservations.FirstAsync(r => r.Id == rejected.Id);
        Assert.Equal(ReservationStatus.Rejected, verifiedRejected.Status);
        Assert.Equal(rejected.UpdatedAt, verifiedRejected.UpdatedAt);
    }

    [Fact]
    public async Task VariasHeldVencidas_TodasSeDevuelvenYQuedanExpired()
    {
        await _fixture.ResetAsync();

        var vencidas = Enumerable.Range(0, 5)
            .Select(i => TestData.BuildHeldReservation(expiresAt: FixedNow.AddSeconds(-i - 1)))
            .ToArray();
        var vigente = TestData.BuildHeldReservation(expiresAt: FixedNow.AddMinutes(1));

        await SeedAsync([.. vencidas, vigente]);

        await using var ctx = _fixture.CreateContext();
        var expired = await new ReservationRepository(ctx).ExpireHeldAsync(FixedNow, CancellationToken.None);

        Assert.Equal(5, expired.Count);
        Assert.Equal(vencidas.Select(r => r.Id).ToHashSet(), expired.Select(r => r.Id).ToHashSet());

        foreach (var reservation in expired)
        {
            Assert.Equal(ReservationStatus.Expired, reservation.Status);
            Assert.Equal(FixedNow, reservation.UpdatedAt);
        }

        // La vigente del mismo lote no se toca.
        await using var verifyCtx = _fixture.CreateContext();
        var persistedVigente = await verifyCtx.Reservations.FirstAsync(r => r.Id == vigente.Id);
        Assert.Equal(ReservationStatus.Held, persistedVigente.Status);
    }

    [Fact]
    public async Task TablaVacia_DevuelveListaVacia()
    {
        await _fixture.ResetAsync();

        await using var ctx = _fixture.CreateContext();
        var expired = await new ReservationRepository(ctx).ExpireHeldAsync(FixedNow, CancellationToken.None);

        Assert.Empty(expired);
    }

    [Fact]
    public async Task Vencer_LiberaElAsiento()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        await SeedAsync(TestData.BuildHeldReservation(
            eventId: eventId,
            seatId: seatId,
            expiresAt: FixedNow.AddSeconds(-1)));

        await using var ctx = _fixture.CreateContext();
        var expired = await new ReservationRepository(ctx).ExpireHeldAsync(FixedNow, CancellationToken.None);

        Assert.Single(expired);

        // Ahora TryHoldAsync sobre ese asiento devuelve Created.
        var second = TestData.BuildHeldReservation(eventId: eventId, seatId: seatId);
        await using var ctx2 = _fixture.CreateContext();
        var holdResult = await new ReservationRepository(ctx2).TryHoldAsync(second, CancellationToken.None);

        Assert.Equal(HoldResult.Created, holdResult);
    }

    [Fact]
    public async Task EntidadesDevueltas_NoQuedanTrackeadas()
    {
        await _fixture.ResetAsync();

        await SeedAsync(TestData.BuildHeldReservation(expiresAt: FixedNow.AddSeconds(-1)));

        // Contexto limpio: si el repositorio dejara entidades trackeadas, se verían acá.
        await using var ctx = _fixture.CreateContext();
        var expired = await new ReservationRepository(ctx).ExpireHeldAsync(FixedNow, CancellationToken.None);

        Assert.Single(expired);
        Assert.Empty(ctx.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ReplicasEnParalelo_DiezTareasConVeinteHeldVencidas()
    {
        await _fixture.ResetAsync();

        // Se siembran 20 Held vencidas.
        var sembradas = Enumerable.Range(0, 20)
            .Select(i => TestData.BuildHeldReservation(expiresAt: FixedNow.AddSeconds(-i - 1)))
            .ToArray();
        await SeedAsync(sembradas);

        // 10 tareas en paralelo (10 "réplicas" del job), cada una con su propio contexto.
        // El TaskCompletionSource las libera a todas a la vez, sin bloquear hilos.
        var gate = new TaskCompletionSource<bool>();
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            await gate.Task;

            await using var taskCtx = _fixture.CreateContext();
            return await new ReservationRepository(taskCtx).ExpireHeldAsync(FixedNow, CancellationToken.None);
        })).ToList();

        gate.SetResult(true);
        var results = await Task.WhenAll(tasks);

        // La suma de lo devuelto por todas las réplicas debe ser exactamente 20...
        var allExpired = results.SelectMany(r => r).ToList();
        Assert.Equal(20, allExpired.Count);

        // ...sin ningún Id repetido (ninguna fila se venció dos veces)...
        var expiredIds = allExpired.Select(r => r.Id).ToHashSet();
        Assert.Equal(20, expiredIds.Count);
        Assert.Equal(sembradas.Select(r => r.Id).ToHashSet(), expiredIds);

        // ...y todas quedan Expired.
        Assert.All(allExpired, r => Assert.Equal(ReservationStatus.Expired, r.Status));
    }

    [Fact]
    public async Task CarreraResolverVsVencer_ExactamenteUnaGanaYElEstadoEsCoherente()
    {
        await _fixture.ResetAsync();

        const int escenarios = 20;

        for (var escenario = 0; escenario < escenarios; escenario++)
        {
            // Una reserva distinta por escenario, para que la carrera ocurra de verdad cada vez.
            var held = TestData.BuildHeldReservation(expiresAt: FixedNow);
            await SeedAsync(held);

            var gate = new TaskCompletionSource<bool>();

            // Con now = T - 1 s, TryResolveHeldAsync ve la reserva vigente (T > T - 1 s).
            // Con now = T, ExpireHeldAsync la ve vencida (T <= T). Las dos son elegibles a la
            // vez y ambas exigen status = 'Held': solo una puede tener efecto.
            var resolveTask = Task.Run(async () =>
            {
                await gate.Task;

                await using var resolveCtx = _fixture.CreateContext();
                return await new ReservationRepository(resolveCtx).TryResolveHeldAsync(
                    held.Id,
                    ReservationStatus.Confirmed,
                    null,
                    FixedNow.AddSeconds(-1),
                    CancellationToken.None);
            });

            var expireTask = Task.Run(async () =>
            {
                await gate.Task;

                await using var expireCtx = _fixture.CreateContext();
                var vencidas = await new ReservationRepository(expireCtx)
                    .ExpireHeldAsync(FixedNow, CancellationToken.None);
                return vencidas.Any(r => r.Id == held.Id);
            });

            gate.SetResult(true);
            var resolverGano = await resolveTask;
            var vencerGano = await expireTask;

            // Exactamente una de las dos operaciones debe haber tenido efecto.
            // No se exige que gane cada una alguna vez: quién gana depende del scheduler.
            Assert.True(
                resolverGano ^ vencerGano,
                $"Escenario {escenario}: resolver={resolverGano}, vencer={vencerGano}");

            // El estado final debe ser coherente con la operación que ganó.
            await using var verifyCtx = _fixture.CreateContext();
            var finalReservation = await verifyCtx.Reservations.FirstAsync(r => r.Id == held.Id);

            var estadoEsperado = resolverGano ? ReservationStatus.Confirmed : ReservationStatus.Expired;
            Assert.Equal(estadoEsperado, finalReservation.Status);
        }
    }
}