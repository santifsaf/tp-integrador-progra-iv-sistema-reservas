using Booking.Data.Entities;
using Booking.Data.Persistence;
using Xunit;

namespace Booking.Data.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class ReservationRepositoryHoldTests
{
    private readonly PostgresFixture _fixture;

    public ReservationRepositoryHoldTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private Reservation BuildHeldReservation(
        Guid? requestId = null,
        Guid? eventId = null,
        Guid? seatId = null,
        DateTimeOffset? expiresAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Reservation
        {
            Id = Guid.NewGuid(),
            RequestId = requestId ?? Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            EventId = eventId ?? Guid.NewGuid(),
            SeatId = seatId ?? Guid.NewGuid(),
            Status = ReservationStatus.Held,
            ExpiresAt = expiresAt ?? now.AddMinutes(2),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task<HoldResult> ExecuteTryHoldAsync(Reservation reservation)
    {
        // Un contexto nuevo para cada operación.
        // ReservationRepository no es thread-safe, así que cada test obtiene su propia instancia.
        await using var context = _fixture.CreateContext();
        var repository = new ReservationRepository(context);
        return await repository.TryHoldAsync(reservation, CancellationToken.None);
    }

    [Fact]
    public async Task GetByRequestIdAsync_SinReservaExistente_DevuelveNull()
    {
        await _fixture.ResetAsync();

        await using var context = _fixture.CreateContext();
        var repository = new ReservationRepository(context);
        var requestId = Guid.NewGuid();

        var result = await repository.GetByRequestIdAsync(requestId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryHoldAsync_AsientoLibre_DevuelveCreatedYPersiste()
    {
        await _fixture.ResetAsync();

        var reservation = BuildHeldReservation();

        var result = await ExecuteTryHoldAsync(reservation);

        Assert.Equal(HoldResult.Created, result);

        // Verificar que quedó guardada en un contexto distinto (sin cache en memoria).
        await using var verifyContext = _fixture.CreateContext();
        var verifyRepository = new ReservationRepository(verifyContext);
        var persisted = await verifyRepository.GetByRequestIdAsync(reservation.RequestId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(reservation.Id, persisted.Id);
        Assert.Equal(ReservationStatus.Held, persisted.Status);
    }

    [Fact]
    public async Task TryHoldAsync_MismoRequestIdDistintoIdReserva_DevuelveDuplicateRequest()
    {
        await _fixture.ResetAsync();

        var requestId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var first = BuildHeldReservation(requestId: requestId, eventId: eventId, seatId: seatId);
        await ExecuteTryHoldAsync(first);

        var second = BuildHeldReservation(requestId: requestId, eventId: eventId, seatId: seatId);
        var result = await ExecuteTryHoldAsync(second);

        Assert.Equal(HoldResult.DuplicateRequest, result);
    }

    [Fact]
    public async Task TryHoldAsync_MismoAsientoDistintoRequest_DevuelveSeatOccupied()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var first = BuildHeldReservation(eventId: eventId, seatId: seatId);
        await ExecuteTryHoldAsync(first);

        var second = BuildHeldReservation(eventId: eventId, seatId: seatId);
        var result = await ExecuteTryHoldAsync(second);

        Assert.Equal(HoldResult.SeatOccupied, result);
    }

    [Fact]
    public async Task TryHoldAsync_DespuesDelSeatOccupied_PuedoInsertarEnOtroAsiento()
    {
        await _fixture.ResetAsync();

        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        var first = BuildHeldReservation(eventId: eventId, seatId: seatId);
        await ExecuteTryHoldAsync(first);

        // Reutilizo el mismo contexto y repositorio tras el fallo.
        // Esto prueba que la entidad fue descartada (Detached) tras el fallo.
        await using var contextReused = _fixture.CreateContext();
        var repositoryReused = new ReservationRepository(contextReused);

        var failed = BuildHeldReservation(eventId: eventId, seatId: seatId);
        var failResult = await repositoryReused.TryHoldAsync(failed, CancellationToken.None);
        Assert.Equal(HoldResult.SeatOccupied, failResult);

        // Mismo contexto, pero otro asiento. La entidad fallida no interfiere.
        var success = BuildHeldReservation(eventId: eventId, seatId: Guid.NewGuid());
        var successResult = await repositoryReused.TryHoldAsync(success, CancellationToken.None);

        Assert.Equal(HoldResult.Created, successResult);
    }
}
