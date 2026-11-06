using System.Net;
using System.Text;
using Booking.Api.Payments;
using Booking.Business.Payments;
using Booking.Data.Entities;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SeatReservation.Contracts.Payment;
using Xunit;

namespace Booking.Api.Tests;

public sealed class ReservationPaymentClientTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(3_000_000_123L)]
    public async Task Charge_SendsReservationDataAndOriginalCancellationToken(long amountCents)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var reservation = ValidReservation();
        reservation.AmountCents = amountCents;

        var result = await fixture.Client.ChargeAsync(reservation, cancellation.Token);

        Assert.Equal(PaymentResult.Succeeded, result);
        var call = Assert.Single(fixture.Invoker.Calls);
        Assert.Equal("Charge", call.Method);
        var request = Assert.IsType<ChargeRequest>(call.Request);
        Assert.Equal(reservation.Id.ToString("D"), request.ReservationId);
        Assert.Equal(reservation.Id.ToString("D"), request.PaymentOperationId);
        Assert.Equal(amountCents, request.AmountCents);
        Assert.Equal("USD", request.Currency);
        Assert.Equal(reservation.RequestId.ToString("D"),
            Assert.Single(call.Options.Headers!, h => h.Key == "x-correlation-id").Value);
        Assert.Equal(cancellation.Token, call.Options.CancellationToken);
        Assert.Equal(1, fixture.TokenRequests);
    }

    [Fact]
    public async Task RetryWithAnotherAdapterAndReservationInstance_PreservesLogicalCharge()
    {
        using var fixture = new Fixture();
        var reservation = ValidReservation();
        fixture.Invoker.Response = _ => fixture.Invoker.Calls.Count == 1
            ? Task.FromException<ChargeResponse>(new RpcException(new Status(StatusCode.Unavailable, "offline")))
            : Task.FromResult(new ChargeResponse { Status = PaymentStatus.PaymentSucceeded });

        Assert.Equal(PaymentResult.Uncertain,
            await fixture.Client.ChargeAsync(reservation, CancellationToken.None));
        var retry = new Reservation
        {
            Id = reservation.Id,
            RequestId = reservation.RequestId,
            AmountCents = reservation.AmountCents,
            Currency = reservation.Currency,
            Status = ReservationStatus.Held
        };
        IPaymentClient anotherAdapter = new ReservationPaymentClient(fixture.Gateway);

        Assert.Equal(PaymentResult.Succeeded,
            await anotherAdapter.ChargeAsync(retry, CancellationToken.None));

        Assert.Equal(2, fixture.Invoker.Calls.Count);
        Assert.All(fixture.Invoker.Calls, call =>
        {
            Assert.Equal("Charge", call.Method);
            var request = Assert.IsType<ChargeRequest>(call.Request);
            Assert.Equal(reservation.Id.ToString("D"), request.ReservationId);
            Assert.Equal(reservation.Id.ToString("D"), request.PaymentOperationId);
            Assert.Equal(reservation.AmountCents, request.AmountCents);
            Assert.Equal(reservation.Currency, request.Currency);
        });
        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Equal(ReservationStatus.Held, retry.Status);
    }

    [Theory]
    [InlineData(PaymentStatus.PaymentSucceeded, PaymentResult.Succeeded)]
    [InlineData(PaymentStatus.PaymentRejected, PaymentResult.Failed)]
    [InlineData(PaymentStatus.PaymentUnavailable, PaymentResult.Unavailable)]
    [InlineData(PaymentStatus.PaymentPending, PaymentResult.Uncertain)]
    public async Task PaymentResult_IsReturnedWithoutChangingReservationState(
        PaymentStatus status, PaymentResult expected)
    {
        using var fixture = new Fixture();
        fixture.Invoker.Response = _ => Task.FromResult(new ChargeResponse { Status = status });
        var reservation = ValidReservation();
        var originalUpdatedAt = reservation.UpdatedAt;

        Assert.Equal(expected, await fixture.Client.ChargeAsync(reservation, CancellationToken.None));

        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Null(reservation.RejectionReason);
        Assert.Equal(originalUpdatedAt, reservation.UpdatedAt);
    }

    [Theory]
    [InlineData("requestId")]
    [InlineData("reservationId")]
    [InlineData("zeroAmount")]
    [InlineData("negativeAmount")]
    [InlineData("nullCurrency")]
    [InlineData("emptyCurrency")]
    [InlineData("whitespaceCurrency")]
    [InlineData("lowercaseCurrency")]
    [InlineData("longCurrency")]
    public async Task InvalidData_ThrowsBeforeAuthenticationOrPaymentAndDoesNotRejectReservation(string field)
    {
        using var fixture = new Fixture();
        var reservation = ValidReservation();
        switch (field)
        {
            case "requestId": reservation.RequestId = Guid.Empty; break;
            case "reservationId": reservation.Id = Guid.Empty; break;
            case "zeroAmount": reservation.AmountCents = 0; break;
            case "negativeAmount": reservation.AmountCents = -1; break;
            case "nullCurrency": reservation.Currency = null!; break;
            case "emptyCurrency": reservation.Currency = string.Empty; break;
            case "whitespaceCurrency": reservation.Currency = "   "; break;
            case "lowercaseCurrency": reservation.Currency = "ars"; break;
            case "longCurrency": reservation.Currency = "ARSX"; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            fixture.Client.ChargeAsync(reservation, CancellationToken.None));

        Assert.Equal(0, fixture.TokenRequests);
        Assert.Empty(fixture.Invoker.Calls);
        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Null(reservation.RejectionReason);
    }

    [Fact]
    public async Task MissingPaymentData_IsNotReplacedWithDefaultsOrARejectedPaymentResult()
    {
        using var fixture = new Fixture();
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(), RequestId = Guid.NewGuid(), Status = ReservationStatus.Held
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Client.ChargeAsync(reservation, CancellationToken.None));

        Assert.Equal(0, reservation.AmountCents);
        Assert.Empty(reservation.Currency);
        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Null(reservation.RejectionReason);
        Assert.Equal(0, fixture.TokenRequests);
        Assert.Empty(fixture.Invoker.Calls);
    }

    [Fact]
    public async Task NullReservation_IsRejectedBeforeAnyNetworkCall()
    {
        using var fixture = new Fixture();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            fixture.Client.ChargeAsync(null!, CancellationToken.None));

        Assert.Equal(0, fixture.TokenRequests);
        Assert.Empty(fixture.Invoker.Calls);
    }

    [Fact]
    public async Task CallerCancellationDuringPayment_PropagatesWithoutChangingReservationState()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Invoker.Response = _ =>
        {
            cancellation.Cancel();
            return Task.FromException<ChargeResponse>(new OperationCanceledException(cancellation.Token));
        };
        var reservation = ValidReservation();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Client.ChargeAsync(reservation, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(cancellation.Token, Assert.Single(fixture.Invoker.Calls).Options.CancellationToken);
        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Null(reservation.RejectionReason);
    }

    [Fact]
    public async Task AlreadyCanceledCaller_DoesNotAuthenticateOrCallPayment()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Client.ChargeAsync(ValidReservation(), cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, fixture.TokenRequests);
        Assert.Empty(fixture.Invoker.Calls);
    }

    private static Reservation ValidReservation() => new()
    {
        Id = Guid.NewGuid(), RequestId = Guid.NewGuid(), AmountCents = 3_000_000_123,
        Currency = "USD", Status = ReservationStatus.Held, UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        public RecordingInvoker Invoker { get; } = new();
        public GrpcPaymentGateway Gateway { get; }
        public IPaymentClient Client { get; }
        public int TokenRequests { get; private set; }

        public Fixture()
        {
            var options = Options.Create(new PaymentClientOptions
            {
                TokenEndpoint = "https://identity.example/token", ClientId = "booking", ClientSecret = "test-secret"
            });
            _http = new HttpClient(new TokenHandler(this));
            Gateway = new(new PaymentService.PaymentServiceClient(Invoker),
                new PaymentTokenProvider(_http, options), options, TimeProvider.System,
                NullLogger<GrpcPaymentGateway>.Instance);
            Client = new ReservationPaymentClient(Gateway);
        }

        public void Dispose() => _http.Dispose();

        private sealed class TokenHandler(Fixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                fixture.TokenRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"test-token\",\"token_type\":\"Bearer\"}",
                        Encoding.UTF8, "application/json")
                });
            }
        }
    }

    private sealed record RecordedCall(string Method, object Request, CallOptions Options);

    // Se usa el gateway real con transporte de prueba para verificar también que
    // la validación se ejecute antes de solicitar credenciales o enviar un cobro.
    private sealed class RecordingInvoker : CallInvoker
    {
        public List<RecordedCall> Calls { get; } = [];
        public Func<CallOptions, Task<ChargeResponse>> Response { get; set; } =
            _ => Task.FromResult(new ChargeResponse { Status = PaymentStatus.PaymentSucceeded });

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Calls.Add(new(method.Name, request, options));
            async Task<TResponse> Reply() => (TResponse)(object)await Response(options);
            return new(Reply(), Task.FromResult(new Metadata()), () => Status.DefaultSuccess,
                () => new Metadata(), () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method,
            string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
