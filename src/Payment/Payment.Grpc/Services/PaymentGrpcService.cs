using IPaymentBusinessService = Payment.Business.Services.IPaymentService;
using Grpc.Core;
using SeatReservation.Contracts.Payment;
using Payment.Business.Exceptions;

namespace Payment.Grpc.Services;

public class PaymentGrpcService : PaymentService.PaymentServiceBase
{
    private readonly IPaymentBusinessService _paymentService;

    public PaymentGrpcService(IPaymentBusinessService paymentService)
    {
        _paymentService = paymentService;
    }

    public override async Task<ChargeResponse> Charge(
        ChargeRequest request,
        ServerCallContext context)
    {
        // Validar los identificadores.
        ValidateGuid(request.ReservationId, "reservation_id");
        ValidateGuid(request.PaymentOperationId, "payment_operation_id");

        // Validar el importe.
        if (request.AmountCents <= 0)
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "amount_cents debe ser mayor que cero."));
        }

        // Validar la moneda.
        if (request.Currency.Length != 3 ||
            request.Currency.Any(c => c < 'A' || c > 'Z'))
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "currency debe tener tres letras mayúsculas, por ejemplo ARS."));
        }

        // Transformar la solicitud al DTO de Business.
        var businessRequest = PaymentGrpcMapper.ToBusinessRequest(request);

        try
        {
            // Procesar el cobro.
            var result = await _paymentService.ChargeAsync(businessRequest);

            // Transformar la respuesta a gRPC.
            return PaymentGrpcMapper.ToGrpcResponse(result);
        }
        catch (PaymentConflictException)
        {
            throw new RpcException(
                new Status(
                    StatusCode.AlreadyExists,
                    "payment_operation_id ya existe con datos diferentes."));
        }

        catch (PaymentValidationException ex)
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    ex.Message));
        }
    }




    public override async Task<ChargeResponse> GetPayment(
    GetPaymentRequest request,
    ServerCallContext context)
    {
        // Validar el identificador antes de consultar Business.
        ValidateGuid(request.PaymentOperationId, "payment_operation_id");

        var result = await _paymentService.GetPaymentAsync(
            Guid.Parse(request.PaymentOperationId).ToString("D"));

        if (result is null)
        {
            throw new RpcException(
                new Status(
                    StatusCode.NotFound,
                    "No se encontró el pago solicitado."));
        }

        return PaymentGrpcMapper.ToGrpcResponse(result);
    }

    private static void ValidateGuid(string value, string fieldName)
    {
        if (!Guid.TryParse(value, out Guid id) || id == Guid.Empty)
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    $"{fieldName} debe ser un GUID válido y no vacío."));
        }
    }
}
