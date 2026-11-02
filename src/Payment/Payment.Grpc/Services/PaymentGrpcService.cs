

using Grpc.Core;
using SeatReservation.Contracts.Payment;

namespace Payment.Grpc.Services;

public class PaymentGrpcService : PaymentService.PaymentServiceBase
{
    public override Task<ChargeResponse> Charge(
        ChargeRequest request,
        ServerCallContext context)
    {
        // Validaciones de la solicitud.
        ValidateGuid(request.ReservationId, "reservation_id");
        ValidateGuid(request.PaymentOperationId, "payment_operation_id");

        if (request.AmountCents <= 0)
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "amount_cents debe ser mayor que cero."));
        }

        if (request.Currency.Length != 3 ||
            request.Currency.Any(c => c < 'A' || c > 'Z'))
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "currency debe tener tres letras mayúsculas, por ejemplo ARS."));
        }

        // Pendiente: conectar con Payment.Business de Juan.
        throw new RpcException(
            new Status(
                StatusCode.Unimplemented,
                "Charge pendiente de integración con Payment.Business."));
    }

    public override Task<ChargeResponse> GetPayment(
        GetPaymentRequest request,
        ServerCallContext context)
    {
        // Validar el identificador de la operación.
        ValidateGuid(request.PaymentOperationId, "payment_operation_id");

        // Pendiente: conectar con Payment.Business de Juan.
        throw new RpcException(
            new Status(
                StatusCode.Unimplemented,
                "GetPayment pendiente de integración con Payment.Business."));
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
