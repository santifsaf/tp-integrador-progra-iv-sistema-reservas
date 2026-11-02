
using Grpc.Core;
using SeatReservation.Contracts.Payment;

namespace Payment.Grpc.Services;

public class PaymentGrpcService : PaymentService.PaymentServiceBase
{
    public override Task<ChargeResponse> Charge(
        ChargeRequest request,
        ServerCallContext context)
    {
        throw new RpcException(
            new Status(
                StatusCode.Unimplemented,
                "Charge pendiente de integración con Payment.Business."));
    }

    public override Task<ChargeResponse> GetPayment(
        GetPaymentRequest request,
        ServerCallContext context)
    {
        throw new RpcException(
            new Status(
                StatusCode.Unimplemented,
                "GetPayment pendiente de integración con Payment.Business."));
    }
}
