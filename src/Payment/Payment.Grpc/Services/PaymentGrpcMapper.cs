
using Payment.Business.Dtos;
using SeatReservation.Contracts.Payment;
using BusinessStatus = Payment.Data.Entities.PaymentStatus;
using GrpcStatus = SeatReservation.Contracts.Payment.PaymentStatus;

namespace Payment.Grpc.Services;

public static class PaymentGrpcMapper
{
    public static ChargeRequestDto ToBusinessRequest(ChargeRequest request)
    {
        return new ChargeRequestDto
        {
            ReservationId = Guid.Parse(request.ReservationId).ToString("D"),
            OperationId = Guid.Parse(request.PaymentOperationId).ToString("D"),
            AmountCents = request.AmountCents,
            Currency = request.Currency
        };
    }

    public static ChargeResponse ToGrpcResponse(PaymentResultDto result)
    {
        return new ChargeResponse
        {
            PaymentId = result.PaymentId,
            Status = result.Status switch
            {
                BusinessStatus.PENDING => GrpcStatus.PaymentPending,
                BusinessStatus.SUCCEEDED => GrpcStatus.PaymentSucceeded,
                BusinessStatus.REJECTED => GrpcStatus.PaymentRejected,
                BusinessStatus.UNAVAILABLE => GrpcStatus.PaymentUnavailable,
                _ => GrpcStatus.Unspecified
            },
            FailureReason = result.FailureReason ?? string.Empty
        };
    }
}
