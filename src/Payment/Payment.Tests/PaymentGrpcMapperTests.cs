
using Payment.Business.Dtos;
using Payment.Grpc.Services;
using SeatReservation.Contracts.Payment;
using Xunit;
using BusinessStatus = Payment.Data.Entities.PaymentStatus;
using GrpcStatus = SeatReservation.Contracts.Payment.PaymentStatus;

namespace Payment.Tests;

public sealed class PaymentGrpcMapperTests
{
    [Fact]
    public void ToBusinessRequest_CopiesAllFields()
    {
        var request = new ChargeRequest
        {
            ReservationId = "11111111-1111-1111-1111-111111111111",
            PaymentOperationId = "22222222-2222-2222-2222-222222222222",
            AmountCents = 10000,
            Currency = "ARS"
        };

        var result = PaymentGrpcMapper.ToBusinessRequest(request);

        Assert.Equal(request.ReservationId, result.ReservationId);
        Assert.Equal(request.PaymentOperationId, result.OperationId);
        Assert.Equal(request.AmountCents, result.AmountCents);
        Assert.Equal(request.Currency, result.Currency);
    }

    [Theory]
    [InlineData(BusinessStatus.PENDING, GrpcStatus.PaymentPending)]
    [InlineData(BusinessStatus.SUCCEEDED, GrpcStatus.PaymentSucceeded)]
    [InlineData(BusinessStatus.REJECTED, GrpcStatus.PaymentRejected)]
    [InlineData(BusinessStatus.UNAVAILABLE, GrpcStatus.PaymentUnavailable)]
    [InlineData(BusinessStatus.UNSPECIFIED, GrpcStatus.Unspecified)]
    public void ToGrpcResponse_MapsPaymentStatus(
        BusinessStatus status,
        GrpcStatus expected)
    {
        var result = new PaymentResultDto
        {
            PaymentId = Guid.NewGuid().ToString(),
            Status = status
        };

        var response = PaymentGrpcMapper.ToGrpcResponse(result);

        Assert.Equal(expected, response.Status);
    }

    [Fact]
    public void ToGrpcResponse_CopiesPaymentIdAndFailureReason()
    {
        var result = new PaymentResultDto
        {
            PaymentId = "payment-123",
            Status = BusinessStatus.REJECTED,
            FailureReason = "Pago rechazado"
        };

        var response = PaymentGrpcMapper.ToGrpcResponse(result);

        Assert.Equal("payment-123", response.PaymentId);
        Assert.Equal("Pago rechazado", response.FailureReason);
    }

    [Fact]
    public void ToGrpcResponse_NullFailureReason_ReturnsEmptyString()
    {
        var result = new PaymentResultDto
        {
            PaymentId = "payment-456",
            Status = BusinessStatus.SUCCEEDED,
            FailureReason = null
        };

        var response = PaymentGrpcMapper.ToGrpcResponse(result);

        Assert.Equal(string.Empty, response.FailureReason);
    }

    [Fact]
    public void ToGrpcResponse_UnknownStatus_ReturnsUnspecified()
    {
        var result = new PaymentResultDto
        {
            Status = (BusinessStatus)999
        };

        var response = PaymentGrpcMapper.ToGrpcResponse(result);

        Assert.Equal(GrpcStatus.Unspecified, response.Status);
    }
}
