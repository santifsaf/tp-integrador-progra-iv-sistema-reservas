using System.Net;
using System.Text;
using Booking.Api.Payments;
using Booking.Business.Payments;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeatReservation.Contracts.Payment;
using Xunit;

namespace Booking.Api.Tests;

public sealed class GrpcPaymentGatewayTests
{
    [Theory]
    [InlineData(PaymentStatus.PaymentSucceeded, PaymentResult.Succeeded)]
    [InlineData(PaymentStatus.PaymentRejected, PaymentResult.Failed)]
    [InlineData(PaymentStatus.PaymentUnavailable, PaymentResult.Unavailable)]
    [InlineData((PaymentStatus)0, PaymentResult.Uncertain)]
    [InlineData((PaymentStatus)1, PaymentResult.Uncertain)]
    [InlineData((PaymentStatus)99, PaymentResult.Uncertain)]
    public async Task BothOperations_MapPaymentStatus(PaymentStatus status, PaymentResult expected)
    {
        using var fixture = new Fixture();
        fixture.Invoker.Reply = (_, _, _) => Task.FromResult(new ChargeResponse { Status = status });
        var charge = ValidCharge();

        Assert.Equal(expected, await fixture.Gateway.ChargeAsync(charge, CancellationToken.None));
        Assert.Equal(expected, await fixture.Gateway.GetPaymentAsync(
            charge.PaymentOperationId, charge.RequestId, CancellationToken.None));
        Assert.Equal(2, fixture.Invoker.Calls.Count);
    }

    [Fact]
    public async Task Charge_SendsExplicitDataTokenCorrelationDeadlineAndCancellation()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var charge = ValidCharge();

        await fixture.Gateway.ChargeAsync(charge, cancellation.Token);

        var call = Assert.Single(fixture.Invoker.Calls);
        Assert.Equal("Charge", call.Method);
        var request = Assert.IsType<ChargeRequest>(call.Request);
        Assert.Equal(charge.ReservationId.ToString("D"), request.ReservationId);
        Assert.Equal(charge.PaymentOperationId.ToString("D"), request.PaymentOperationId);
        Assert.Equal(charge.AmountCents, request.AmountCents);
        Assert.Equal(charge.Currency, request.Currency);
        Assert.Equal("Bearer test-token", Assert.Single(call.Options.Headers!, h => h.Key == "authorization").Value);
        Assert.Equal(charge.RequestId.ToString("D"), Assert.Single(call.Options.Headers!, h => h.Key == "x-correlation-id").Value);
        Assert.Equal(fixture.Clock.GetUtcNow().AddSeconds(5).UtcDateTime, call.Options.Deadline);
        Assert.Equal(cancellation.Token, call.Options.CancellationToken);
        Assert.Contains(fixture.Logs.Messages, m => m.Contains($"CorrelationId={charge.RequestId}"));
    }

    [Fact]
    public async Task RepeatedCharge_PreservesOperationAndOriginalAmount()
    {
        using var fixture = new Fixture();
        var charge = ValidCharge();

        await fixture.Gateway.ChargeAsync(charge, CancellationToken.None);
        await fixture.Gateway.ChargeAsync(charge, CancellationToken.None);

        Assert.Equal(2, fixture.Invoker.Calls.Count);
        Assert.All(fixture.Invoker.Calls, call =>
        {
            var request = Assert.IsType<ChargeRequest>(call.Request);
            Assert.Equal(charge.PaymentOperationId.ToString("D"), request.PaymentOperationId);
            Assert.Equal(charge.AmountCents, request.AmountCents);
            Assert.Equal(charge.Currency, request.Currency);
        });
    }

    [Fact]
    public async Task GetPayment_OnlyQueriesTheSuppliedOperation()
    {
        using var fixture = new Fixture();
        var charge = ValidCharge();

        await fixture.Gateway.GetPaymentAsync(charge.PaymentOperationId, charge.RequestId, CancellationToken.None);

        var call = Assert.Single(fixture.Invoker.Calls);
        Assert.Equal("GetPayment", call.Method);
        Assert.Equal(charge.PaymentOperationId.ToString("D"), Assert.IsType<GetPaymentRequest>(call.Request).PaymentOperationId);
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.Cancelled)]
    [InlineData(StatusCode.Internal)]
    public async Task RpcFailure_IsUncertainAndDoesNotRetry(StatusCode code)
    {
        using var fixture = new Fixture();
        fixture.Invoker.Reply = (_, _, _) => Task.FromException<ChargeResponse>(
            new RpcException(new Status(code, "remote-detail test-token test-secret")));

        var result = await fixture.Gateway.ChargeAsync(ValidCharge(), CancellationToken.None);

        Assert.Equal(PaymentResult.Uncertain, result);
        Assert.Single(fixture.Invoker.Calls);
        Assert.DoesNotContain(fixture.Logs.Messages, m => m.Contains("test-token") || m.Contains("test-secret"));
        Assert.Single(fixture.Logs.Messages, m => m.Contains("[AUDIT] END"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenFailure_DoesNotCallPaymentOrRejectReservation(bool timeout)
    {
        using var fixture = new Fixture();
        fixture.TokenReply = () => timeout
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException())
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await fixture.Gateway.ChargeAsync(ValidCharge(), CancellationToken.None);

        Assert.Equal(PaymentResult.Uncertain, result);
        Assert.Empty(fixture.Invoker.Calls);
    }

    [Fact]
    public async Task GrpcDeadlineReportedAsCancellation_IsUncertainAndIdentifiesGrpcStage()
    {
        using var fixture = new Fixture();
        fixture.Invoker.Reply = (_, _, _) =>
            Task.FromException<ChargeResponse>(new OperationCanceledException());

        var result = await fixture.Gateway.ChargeAsync(ValidCharge(), CancellationToken.None);

        Assert.Equal(PaymentResult.Uncertain, result);
        Assert.Single(fixture.Invoker.Calls);
        Assert.Contains(fixture.Logs.Messages, m => m.Contains("Stage=Grpc"));
    }

    [Fact]
    public async Task CallerCancellationDuringRpc_PropagatesAndClosesAudit()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Invoker.Reply = (_, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromException<ChargeResponse>(new RpcException(new Status(StatusCode.Cancelled, "canceled")));
        };

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Gateway.ChargeAsync(ValidCharge(), cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Contains(fixture.Logs.Messages, m => m.Contains("Outcome=Canceled"));
    }

    [Theory]
    [InlineData("requestId")]
    [InlineData("reservationId")]
    [InlineData("operationId")]
    [InlineData("amount")]
    [InlineData("zeroAmount")]
    [InlineData("currency")]
    public async Task InvalidCharge_IsRejectedBeforeAnyNetworkCall(string field)
    {
        using var fixture = new Fixture();
        var valid = ValidCharge();
        var charge = field switch
        {
            "requestId" => valid with { RequestId = Guid.Empty },
            "reservationId" => valid with { ReservationId = Guid.Empty },
            "operationId" => valid with { PaymentOperationId = Guid.Empty },
            "amount" => valid with { AmountCents = -1 },
            "zeroAmount" => valid with { AmountCents = 0 },
            _ => valid with { Currency = "ars" },
        };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Gateway.ChargeAsync(charge, CancellationToken.None));

        Assert.Empty(fixture.Invoker.Calls);
        Assert.Equal(0, fixture.TokenRequests);
    }

    private static PaymentCharge ValidCharge() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 150050, "ARS");

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        public RecordingInvoker Invoker { get; } = new();
        public RecordingLogger Logs { get; } = new();
        public TimeProvider Clock { get; } = new FixedClock();
        public int TokenRequests { get; private set; }
        public Func<Task<HttpResponseMessage>> TokenReply { get; set; } = () => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"test-token\",\"token_type\":\"Bearer\"}", Encoding.UTF8, "application/json"),
            });
        public GrpcPaymentGateway Gateway { get; }

        public Fixture()
        {
            var options = Options.Create(new PaymentClientOptions
            {
                TokenEndpoint = "https://identity.example/token", ClientId = "booking", ClientSecret = "test-secret",
            });
            _http = new HttpClient(new TokenHandler(this));
            Gateway = new(new PaymentService.PaymentServiceClient(Invoker),
                new PaymentTokenProvider(_http, options), options, Clock, Logs);
        }

        public void Dispose() => _http.Dispose();

        private sealed class TokenHandler(Fixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                fixture.TokenRequests++;
                return fixture.TokenReply();
            }
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed record RecordedCall(string Method, object Request, CallOptions Options);

    // El cliente generado usa este CallInvoker de prueba: se inspeccionan las llamadas
    // reales que arma el gateway sin levantar Payment ni simular su lógica de negocio.
    private sealed class RecordingInvoker : CallInvoker
    {
        public List<RecordedCall> Calls { get; } = [];
        public Func<string, object, CallOptions, Task<ChargeResponse>> Reply { get; set; } =
            (_, _, _) => Task.FromResult(new ChargeResponse { Status = PaymentStatus.PaymentSucceeded });

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Calls.Add(new(method.Name, request, options));
            async Task<TResponse> Response() => (TResponse)(object)await Reply(method.Name, request, options);
            return new(Response(), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host,
            CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }

    private sealed class RecordingLogger : ILogger<GrpcPaymentGateway>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, error));
    }
}
