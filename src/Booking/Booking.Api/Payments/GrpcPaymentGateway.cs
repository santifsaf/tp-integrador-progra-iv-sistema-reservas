using Booking.Business.Payments;
using Grpc.Core;
using Microsoft.Extensions.Options;
using SeatReservation.Contracts.Payment;

namespace Booking.Api.Payments;

// Adapter/Gateway: concentra el protocolo y traduce las respuestas de Payment
// al PaymentResult existente en Business. Todavía no implementa IPaymentClient:
// esa interfaz recibe una Reservation que no contiene importe ni moneda.
public sealed class GrpcPaymentGateway(
    PaymentService.PaymentServiceClient client,
    PaymentTokenProvider tokens,
    IOptions<PaymentClientOptions> options,
    TimeProvider clock,
    ILogger<GrpcPaymentGateway> logger)
{
    private readonly PaymentClientOptions _options = options.Value;

    public Task<PaymentResult> ChargeAsync(PaymentCharge charge, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(charge);
        RequireId(charge.RequestId, nameof(charge.RequestId));
        RequireId(charge.ReservationId, nameof(charge.ReservationId));
        RequireId(charge.PaymentOperationId, nameof(charge.PaymentOperationId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(charge.AmountCents);
        if (charge.Currency is not { Length: 3 } || charge.Currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency debe tener tres letras mayúsculas, por ejemplo ARS.", nameof(charge));

        var request = new ChargeRequest
        {
            ReservationId = charge.ReservationId.ToString("D"),
            PaymentOperationId = charge.PaymentOperationId.ToString("D"),
            AmountCents = charge.AmountCents,
            Currency = charge.Currency,
        };
        return ExecuteAsync("Charge", charge.RequestId, charge.PaymentOperationId,
            (headers, deadline, token) => client.ChargeAsync(request, headers, deadline, token), ct);
    }

    public Task<PaymentResult> GetPaymentAsync(Guid paymentOperationId, Guid requestId, CancellationToken ct)
    {
        RequireId(paymentOperationId, nameof(paymentOperationId));
        RequireId(requestId, nameof(requestId));
        var request = new GetPaymentRequest { PaymentOperationId = paymentOperationId.ToString("D") };
        // Consultar no inicia un cobro ni llama a Charge si la operación no existe.
        return ExecuteAsync("GetPayment", requestId, paymentOperationId,
            (headers, deadline, token) => client.GetPaymentAsync(request, headers, deadline, token), ct);
    }

    private async Task<PaymentResult> ExecuteAsync(string operation, Guid requestId, Guid paymentOperationId,
        Func<Metadata, DateTime, CancellationToken, AsyncUnaryCall<ChargeResponse>> call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "Booking",
            ["CorrelationId"] = requestId,
            ["PaymentOperationId"] = paymentOperationId,
        });
        logger.LogInformation(
            "[AUDIT] START {Operation} | ServiceName=Booking CorrelationId={CorrelationId} PaymentOperationId={PaymentOperationId}",
            operation, requestId, paymentOperationId);
        var outcome = "Error";
        var stage = "Authentication";
        try
        {
            var token = await tokens.GetTokenAsync(ct);
            stage = "Grpc";
            var headers = new Metadata
            {
                { "authorization", $"Bearer {token}" },
                { "x-correlation-id", requestId.ToString("D") },
            };
            var deadline = clock.GetUtcNow().AddSeconds(_options.DeadlineSeconds).UtcDateTime;
            using var pending = call(headers, deadline, ct);
            var response = await pending.ResponseAsync;
            var result = response.Status switch
            {
                PaymentStatus.PaymentSucceeded => PaymentResult.Succeeded,
                PaymentStatus.PaymentRejected => PaymentResult.Failed,
                PaymentStatus.PaymentUnavailable => PaymentResult.Unavailable,
                _ => PaymentResult.Uncertain,
            };
            outcome = result.ToString();
            return result;
        }
        catch (RpcException) when (ct.IsCancellationRequested)
        {
            outcome = "Canceled";
            throw new OperationCanceledException(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = "Canceled";
            throw;
        }
        catch (RpcException error)
        {
            // Un fallo de transporte no prueba que Payment no haya cobrado.
            // Se registra el código, sin detalles remotos que puedan contener datos sensibles.
            logger.LogWarning("Resultado incierto de {Operation}. GrpcStatus={GrpcStatus}", operation, error.StatusCode);
            outcome = nameof(PaymentResult.Uncertain);
            return PaymentResult.Uncertain;
        }
        catch (HttpRequestException error)
        {
            logger.LogWarning("No se completó {Operation}. Stage={Stage} HttpStatus={HttpStatus}", operation, stage, error.StatusCode);
            outcome = nameof(PaymentResult.Uncertain);
            return PaymentResult.Uncertain;
        }
        catch (OperationCanceledException)
        {
            // El cliente gRPC también puede representar un deadline agotado con
            // OperationCanceledException. Se distingue de la cancelación del solicitante.
            logger.LogWarning("Timeout o cancelación interna de {Operation}. Stage={Stage}", operation, stage);
            outcome = nameof(PaymentResult.Uncertain);
            return PaymentResult.Uncertain;
        }
        finally
        {
            logger.LogInformation(
                "[AUDIT] END {Operation} | ServiceName=Booking CorrelationId={CorrelationId} PaymentOperationId={PaymentOperationId} Outcome={Outcome}",
                operation, requestId, paymentOperationId, outcome);
        }
    }

    private static void RequireId(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException("El identificador no puede estar vacío.", name);
    }
}
