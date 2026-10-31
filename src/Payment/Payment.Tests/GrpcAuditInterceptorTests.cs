using Grpc.Core;
using Microsoft.Extensions.Logging;
using Payment.Grpc.Auditing;
using Xunit;

namespace Payment.Tests;

// Cubre el punto 3f en Payment: el interceptor tiene que registrar el inicio y el fin de
// cada llamada con el correlation id que vino en la metadata, incluso cuando el handler
// lanza un error (ahí el "fin" cambia el Outcome).
public sealed class GrpcAuditInterceptorTests
{
    [Fact]
    public async Task UnaryCall_LogsStartAndEndWithTheCorrelationIdFromMetadata()
    {
        var logger = new CapturingLogger<GrpcAuditInterceptor>();
        var interceptor = new GrpcAuditInterceptor(logger);
        var context = new FakeServerCallContext(
            "payment.PaymentService/Charge",
            new Metadata { { CorrelationId.MetadataKey, "corr-1001" } });

        var response = await interceptor.UnaryServerHandler(
            request: "charge-request",
            context: context,
            continuation: (_, _) => Task.FromResult("charge-response"));

        Assert.Equal("charge-response", response);
        Assert.Contains(logger.Messages, message => message.Contains("START") && message.Contains("corr-1001"));
        Assert.Contains(
            logger.Messages,
            message => message.Contains("END")
                && message.Contains("corr-1001")
                && message.Contains("Outcome=OK")
                && message.Contains("payment.PaymentService/Charge"));
    }

    [Fact]
    public async Task UnaryCall_WithoutHeader_GeneratesACorrelationId()
    {
        var logger = new CapturingLogger<GrpcAuditInterceptor>();
        var interceptor = new GrpcAuditInterceptor(logger);
        var context = new FakeServerCallContext("payment.PaymentService/GetPayment", new Metadata());

        await interceptor.UnaryServerHandler(
            request: "get-request",
            context: context,
            continuation: (_, _) => Task.FromResult("get-response"));

        var start = Assert.Single(logger.Messages, message => message.Contains("START"));
        Assert.Matches("CorrelationId=[0-9a-f]{32}", start);
    }

    [Fact]
    public async Task RpcException_IsAuditedWithTheGrpcStatusAndRethrown()
    {
        var logger = new CapturingLogger<GrpcAuditInterceptor>();
        var interceptor = new GrpcAuditInterceptor(logger);
        var context = new FakeServerCallContext(
            "payment.PaymentService/Charge",
            new Metadata { { CorrelationId.MetadataKey, "corr-2002" } });

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => interceptor.UnaryServerHandler<string, string>(
                request: "charge-request",
                context: context,
                continuation: (_, _) => Task.FromException<string>(
                    new RpcException(new Status(StatusCode.FailedPrecondition, "pago rechazado")))));

        Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("END")
                && message.Contains("corr-2002")
                && message.Contains("Outcome=RpcError:FailedPrecondition"));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    // ServerCallContext es abstracto y no se puede instanciar directo. El interceptor sólo
    // usa Method, RequestHeaders y GetHttpContext(); el resto se completa con valores
    // mínimos para poder ejercitarlo sin levantar un servidor gRPC real.
    private sealed class FakeServerCallContext(string method, Metadata requestHeaders) : ServerCallContext
    {
        protected override string MethodCore => method;

        protected override string HostCore => "localhost";

        protected override string PeerCore => "ipv4:127.0.0.1:50000";

        protected override DateTime DeadlineCore => DateTime.MaxValue;

        protected override Metadata RequestHeadersCore => requestHeaders;

        protected override CancellationToken CancellationTokenCore => CancellationToken.None;

        protected override Metadata ResponseTrailersCore { get; } = [];

        protected override Status StatusCore { get; set; }

        protected override WriteOptions? WriteOptionsCore { get; set; }

        protected override AuthContext AuthContextCore { get; } =
            new(null, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(
            ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
    }
}
