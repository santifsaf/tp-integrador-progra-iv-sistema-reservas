using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Payment.Business.Auditing;

namespace Payment.Grpc.Auditing;

// Interceptor de auditoría para TODAS las llamadas gRPC de Payment (consigna 3f).
//
// Ventaja frente a poner logs en cada método del servicio: el inicio y el fin de la cadena
// se registran una sola vez, aunque Agus agregue más RPCs al .proto. El servicio sólo
// agrega logs de negocio (PaymentSucceeded, PaymentRejected) con AuditLog.Step.
//
// También propaga el correlation id de vuelta en el header de respuesta, para que el
// cliente (Booking) pueda asociar su log con el de Payment aun cuando no lo haya mandado.
public sealed class GrpcAuditInterceptor(ILogger<GrpcAuditInterceptor> logger) : Interceptor
{
    public const string CorrelationIdHeader = CorrelationId.MetadataKey;
    private const string EventType = "PaymentRequest";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var correlationId = CorrelationId.Resolve(
            context.RequestHeaders,
            Activity.Current?.TraceId.ToString());

        // GetHttpContext() sólo funciona cuando gRPC está hosteado por ASP.NET Core: en un
        // host alternativo (o en un test) lanza InvalidOperationException. El interceptor no
        // puede romper la llamada por eso, así que se degrada a "sin HttpContext".
        var httpContext = TryGetHttpContext(context);
        var clientId = httpContext?.User.FindFirst("azp")?.Value ?? "unknown";
        using var scope = AuditLog.BeginChain(logger, correlationId, clientId);

        // Se escribe antes de ejecutar el handler: cuando la respuesta empieza a viajar ya
        // no se pueden agregar headers.
        if (httpContext is not null && !httpContext.Response.HasStarted)
        {
            httpContext.Response.Headers[CorrelationIdHeader] = correlationId;
        }

        AuditLog.Start(
            logger,
            correlationId,
            EventType,
            detail: $"Method={context.Method} ClientId={clientId}");

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var response = await continuation(request, context);

            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            AuditLog.End(
                logger,
                correlationId,
                EventType,
                outcome: "OK",
                elapsedMs,
                detail: $"Method={context.Method}");

            return response;
        }
        catch (RpcException exception)
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            AuditLog.End(
                logger,
                correlationId,
                EventType,
                outcome: $"RpcError:{exception.StatusCode}",
                elapsedMs,
                detail: $"Method={context.Method} Detail={exception.Status.Detail}");

            // El interceptor no transforma el error: Booking decide qué hacer con el
            // status (ver IPaymentClient: un error técnico es resultado incierto).
            throw;
        }
        catch (Exception exception)
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            AuditLog.End(
                logger,
                correlationId,
                EventType,
                outcome: "UnhandledException",
                elapsedMs,
                detail: $"Method={context.Method} Exception={exception.GetType().Name}");

            logger.LogError(exception, "Error no controlado procesando {Method}", context.Method);
            throw;
        }
    }

    private static HttpContext? TryGetHttpContext(ServerCallContext context)
    {
        try
        {
            return context.GetHttpContext();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // El contrato PaymentService sólo tiene RPCs unary (Charge y GetPayment). Si en el
    // futuro se agrega streaming, hay que overridear los handlers correspondientes con el
    // mismo patrón para no perder auditoría.
}
