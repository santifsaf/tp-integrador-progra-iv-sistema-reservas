using Booking.Business.Queries;
using Booking.Data;
using Booking.Data.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Booking.Business.Tests;

public sealed class OccupiedSeatsQueryServiceTests
{
    [Fact]
    public async Task Query_ForwardsEventAndCancellationToken()
    {
        var eventId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        Guid[] expected = [Guid.NewGuid(), Guid.NewGuid()];
        var repository = new QueryRepository((id, ct) =>
        {
            Assert.Equal(eventId, id);
            Assert.Equal(cancellation.Token, ct);
            return Task.FromResult<IReadOnlyList<Guid>>(expected);
        });

        var result = await CreateService(repository).GetByEventIdAsync(eventId, cancellation.Token);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task RepositoryFailure_IsNotConvertedToEmptySeats()
    {
        var failure = new InvalidOperationException("repository-failed");
        var repository = new QueryRepository((_, _) => Task.FromException<IReadOnlyList<Guid>>(failure));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(repository).GetByEventIdAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Same(failure, error);
    }

    [Fact]
    public async Task Cancellation_IsNotConvertedToEmptySeats()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new QueryRepository((_, ct) => Task.FromCanceled<IReadOnlyList<Guid>>(ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(repository).GetByEventIdAsync(Guid.NewGuid(), cancellation.Token));
    }

    private static OccupiedSeatsQueryService CreateService(IReservationRepository repository) =>
        new(repository, NullLogger<OccupiedSeatsQueryService>.Instance);

    // Las escrituras fallan: consultar ocupación nunca debe cambiar reservas.
    private sealed class QueryRepository(
        Func<Guid, CancellationToken, Task<IReadOnlyList<Guid>>> read) : IReservationRepository
    {
        public Task<IReadOnlyList<Guid>> GetOccupiedSeatIdsAsync(Guid eventId, CancellationToken ct) => read(eventId, ct);
        public Task<Reservation?> GetByRequestIdAsync(Guid requestId, CancellationToken ct) => throw new NotSupportedException();
        public Task<HoldResult> TryHoldAsync(Reservation reservation, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> AddRejectedAsync(Reservation reservation, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryResolveHeldAsync(Guid reservationId, ReservationStatus to, RejectionReason? reason,
            DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Reservation>> ExpireHeldAsync(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
