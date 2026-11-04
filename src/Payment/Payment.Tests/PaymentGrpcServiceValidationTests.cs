using Payment.Business.Exceptions;
using Payment.Business.Dtos;
using Payment.Business.Services;
using Payment.Data.Entities;
using Grpc.Core;
using Payment.Grpc.Services;
using SeatReservation.Contracts.Payment;
using Xunit;
using BusinessPaymentStatus = Payment.Data.Entities.PaymentStatus;

namespace Payment.Tests;

public sealed class PaymentGrpcServiceValidationTests
{
    private readonly PaymentGrpcService _service = new(new FakePaymentService());

    private const string ValidReservationId =
        "11111111-1111-1111-1111-111111111111";

    private const string ValidOperationId =
        "22222222-2222-2222-2222-222222222222";

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Charge_InvalidReservationId_ReturnsInvalidArgument(
        string reservationId)
    {
        var request = CreateValidChargeRequest();
        request.ReservationId = reservationId;

        await AssertInvalidArgumentAsync(
            () => _service.Charge(request, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Charge_InvalidOperationId_ReturnsInvalidArgument(
        string operationId)
    {
        var request = CreateValidChargeRequest();
        request.PaymentOperationId = operationId;

        await AssertInvalidArgumentAsync(
            () => _service.Charge(request, null!));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Charge_InvalidAmount_ReturnsInvalidArgument(
        long amount)
    {
        var request = CreateValidChargeRequest();
        request.AmountCents = amount;

        await AssertInvalidArgumentAsync(
            () => _service.Charge(request, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ars")]
    [InlineData("AR")]
    [InlineData("ARS1")]
    public async Task Charge_InvalidCurrency_ReturnsInvalidArgument(
        string currency)
    {
        var request = CreateValidChargeRequest();
        request.Currency = currency;

        await AssertInvalidArgumentAsync(
            () => _service.Charge(request, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetPayment_InvalidOperationId_ReturnsInvalidArgument(
        string operationId)
    {
        var request = new GetPaymentRequest
        {
            PaymentOperationId = operationId
        };

        await AssertInvalidArgumentAsync(
            () => _service.GetPayment(request, null!));
    }

    [Fact]
    public async Task Charge_ValidRequest_ReturnsSuccessfulPayment()
    {
        var request = CreateValidChargeRequest();

        var response = await _service.Charge(request, null!);

        Assert.Equal(
            "33333333-3333-3333-3333-333333333333",
            response.PaymentId);

        Assert.Equal(
            SeatReservation.Contracts.Payment.PaymentStatus.PaymentSucceeded,
            response.Status);
    }

    [Fact]
    public async Task GetPayment_ValidRequest_ReturnsSuccessfulPayment()
    {
        var request = new GetPaymentRequest
        {
            PaymentOperationId = ValidOperationId
        };

        var response = await _service.GetPayment(request, null!);

        Assert.Equal(
            "33333333-3333-3333-3333-333333333333",
            response.PaymentId);

        Assert.Equal(
            SeatReservation.Contracts.Payment.PaymentStatus.PaymentSucceeded,
            response.Status);
    }

    private static ChargeRequest CreateValidChargeRequest()
    {
        return new ChargeRequest
        {
            ReservationId = ValidReservationId,
            PaymentOperationId = ValidOperationId,
            AmountCents = 10000,
            Currency = "ARS"
        };
    }

    private static Task AssertInvalidArgumentAsync(
        Func<Task> action)
    {
        return AssertStatusAsync(
            action,
            StatusCode.InvalidArgument);
    }

    private static async Task AssertStatusAsync(
        Func<Task> action,
        StatusCode expectedStatus)
    {
        var exception =
            await Assert.ThrowsAsync<RpcException>(action);

        Assert.Equal(expectedStatus, exception.StatusCode);
    }

    [Fact]
    public async Task Charge_ConflictingOperationId_ReturnsAlreadyExists()
    {
        var service = new PaymentGrpcService(
            new FakePaymentService(throwConflict: true));

        var request = CreateValidChargeRequest();

        await AssertStatusAsync(
            () => service.Charge(request, null!),
            StatusCode.AlreadyExists);
    }

    [Fact]
    public async Task GetPayment_UnknownOperationId_ReturnsNotFound()
    {
        var service = new PaymentGrpcService(
            new FakePaymentService(paymentNotFound: true));

        var request = new GetPaymentRequest
        {
            PaymentOperationId = ValidOperationId
        };

        await AssertStatusAsync(
            () => service.GetPayment(request, null!),
            StatusCode.NotFound);
    }

    private sealed class FakePaymentService(
    bool throwConflict = false,
    bool paymentNotFound = false) : IPaymentService
    {
        public Task<PaymentResultDto> ChargeAsync(ChargeRequestDto request)
        {
            if (throwConflict)
            {
                throw new PaymentConflictException(request.OperationId);
            }

            return Task.FromResult(new PaymentResultDto
            {
                PaymentId = "33333333-3333-3333-3333-333333333333",
                Status = BusinessPaymentStatus.SUCCEEDED
            });
        }

        public Task<PaymentResultDto?> GetPaymentAsync(string operationId)
        {
            if (paymentNotFound)
            {
                return Task.FromResult<PaymentResultDto?>(null);
            }
            return Task.FromResult<PaymentResultDto?>(new PaymentResultDto
            {
                PaymentId = "33333333-3333-3333-3333-333333333333",
                Status = BusinessPaymentStatus.SUCCEEDED
            });
        }
    }
}
