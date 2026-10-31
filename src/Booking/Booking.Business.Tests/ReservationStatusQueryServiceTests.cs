using Booking.Business.Queries;
using Booking.Business.Tests.Fakes;
using Booking.Data.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Booking.Business.Tests;

public sealed class ReservationStatusQueryServiceTests
{
    [Theory]
    [InlineData(ReservationStatus.Held, "HELD")]
    [InlineData(ReservationStatus.Confirmed, "CONFIRMED")]
    [InlineData(ReservationStatus.Rejected, "REJECTED")]
    [InlineData(ReservationStatus.Expired, "EXPIRED")]
    public async Task ExistingReservation_ReturnsTheAgreedStatus(ReservationStatus status, string expected)
    {
        var repository = new InMemoryReservationRepository();
        var requestId = Guid.NewGuid();
        repository.Add(new Reservation { Id = Guid.NewGuid(), RequestId = requestId, Status = status });

        var result = await CreateService(repository).GetByRequestIdAsync(requestId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Status);
        if (status != ReservationStatus.Rejected) Assert.Null(result.Description);
    }

    [Theory]
    [InlineData(RejectionReason.SeatOccupied, "El asiento está ocupado.")]
    [InlineData(RejectionReason.PaymentFailed, "El pago fue rechazado.")]
    [InlineData(RejectionReason.PaymentUnavailable, "El servicio de pago informó que el cobro no está disponible.")]
    public async Task RejectedReservation_ExplainsTheReason(RejectionReason reason, string expected)
    {
        var repository = new InMemoryReservationRepository();
        var requestId = Guid.NewGuid();
        repository.Add(new Reservation
        {
            Id = Guid.NewGuid(), RequestId = requestId,
            Status = ReservationStatus.Rejected, RejectionReason = reason,
        });

        var result = await CreateService(repository).GetByRequestIdAsync(requestId, CancellationToken.None);

        Assert.Equal(expected, result!.Description);
    }

    [Fact]
    public async Task UnknownRequest_ReturnsNull()
    {
        var result = await CreateService(new InMemoryReservationRepository())
            .GetByRequestIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadingAHeldReservation_DoesNotRunExpirationOrChangeStoredState()
    {
        var repository = new InMemoryReservationRepository();
        var requestId = Guid.NewGuid();
        repository.Add(new Reservation
        {
            Id = Guid.NewGuid(), RequestId = requestId,
            Status = ReservationStatus.Held, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        var result = await CreateService(repository).GetByRequestIdAsync(requestId, CancellationToken.None);

        Assert.Equal("HELD", result!.Status);
        Assert.Equal(ReservationStatus.Held, Assert.Single(repository.All).Status);
    }

    private static ReservationStatusQueryService CreateService(InMemoryReservationRepository repository) =>
        new(repository, NullLogger<ReservationStatusQueryService>.Instance);
}
