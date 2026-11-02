
using Grpc.Core;
using Payment.Grpc.Services;
using SeatReservation.Contracts.Payment;
using Xunit;

namespace Payment.Tests;

public sealed class PaymentGrpcServiceValidationTests
{
    private readonly PaymentGrpcService _service = new();

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
    public async Task Charge_ValidRequest_ReachesBusinessIntegration()
    {
        var request = CreateValidChargeRequest();

        await AssertStatusAsync(
            () => _service.Charge(request, null!),
            StatusCode.Unimplemented);
    }

    [Fact]
    public async Task GetPayment_ValidRequest_ReachesBusinessIntegration()
    {
        var request = new GetPaymentRequest
        {
            PaymentOperationId = ValidOperationId
        };

        await AssertStatusAsync(
            () => _service.GetPayment(request, null!),
            StatusCode.Unimplemented);
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
}
