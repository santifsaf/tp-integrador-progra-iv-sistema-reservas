using Booking.Business.Messages;
using Booking.Business.Payments;
using Booking.Business.Services;
using Booking.Business.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Booking.Business.Tests;

public class ReservationRequestedMessageValidatorTests
{
    private static ReservationRequestedMessage Valid() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        DateTimeOffset.UtcNow, 1_500_000, "ARS");

    // Cada caso simula un campo que no vino en el JSON (queda con su valor por defecto)
    // o que vino con un valor que Payment no acepta.
    public static IEnumerable<object[]> InvalidMessages() =>
    [
        ["requestId", Valid() with { RequestId = Guid.Empty }],
        ["eventId", Valid() with { EventId = Guid.Empty }],
        ["seatId", Valid() with { SeatId = Guid.Empty }],
        ["userId", Valid() with { UserId = Guid.Empty }],
        ["requestedAt", Valid() with { RequestedAt = default }],
        ["amountCents", Valid() with { AmountCents = 0 }],
        ["amountCents", Valid() with { AmountCents = -100 }],
        ["currency", Valid() with { Currency = null! }],
        ["currency", Valid() with { Currency = "" }],
        ["currency", Valid() with { Currency = "ars" }],
        ["currency", Valid() with { Currency = "PESOS" }],
    ];

    [Fact]
    public void ValidMessage_HasNoErrors()
    {
        Assert.Empty(ReservationRequestedMessageValidator.Validate(Valid()));
    }

    [Fact]
    public void NullMessage_IsInvalid()
    {
        Assert.NotEmpty(ReservationRequestedMessageValidator.Validate(null));
    }

    [Theory]
    [MemberData(nameof(InvalidMessages))]
    public void MissingOrInvalidField_IsReported(string field, ReservationRequestedMessage message)
    {
        var errors = ReservationRequestedMessageValidator.Validate(message);

        Assert.Contains(errors, e => e.Contains(field));
    }

    [Theory]
    [MemberData(nameof(InvalidMessages))]
    public async Task InvalidMessage_IsNotPersistedNorCharged(string field, ReservationRequestedMessage message)
    {
        var repository = new InMemoryReservationRepository();
        var payment = new FakePaymentClient(PaymentResult.Succeeded);
        var publisher = new FakeEventPublisher();
        var processor = new ReservationRequestProcessor(
            repository, payment, publisher,
            Options.Create(new ReservationsOptions()),
            TimeProvider.System,
            NullLogger<ReservationRequestProcessor>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidReservationRequestException>(
            () => processor.ProcessAsync(message, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Contains(field));
        Assert.Empty(repository.All);      // no se creó ninguna reserva
        Assert.Equal(0, payment.Calls);    // no se pidió ningún cobro
        Assert.Empty(publisher.Published); // no se avisó nada a Notifications
    }
}
