
using Microsoft.Extensions.Logging.Abstractions;
using Payment.Business.Dtos;
using Payment.Business.Exceptions;
using Payment.Business.Services;
using Payment.Data.Entities;
using Payment.Tests.Helpers;
using Xunit;

namespace Payment.Tests
{
    public sealed class PaymentServiceTests
    {
        private readonly FakePaymentRepository _repo = new();
        private readonly PaymentService _service;

        public PaymentServiceTests()
        {
            _service = new PaymentService(_repo, new PaymentSimulator(), NullLogger<PaymentService>.Instance);
        }

        [Fact]
        public async Task Charge_ImporteNormal_DevuelveSucceeded_YGuardaRegistro()
        {
            var result = await _service.ChargeAsync(Request("op-1", 1000));

            Assert.Equal(PaymentStatus.SUCCEEDED, result.Status);
            Assert.Null(result.FailureReason);
            Assert.Equal(1, _repo.Count);
        }

        [Fact]
        public async Task Charge_ImporteTerminadoEn99_DevuelveRejected()
        {
            var result = await _service.ChargeAsync(Request("op-2", 1099));

            Assert.Equal(PaymentStatus.REJECTED, result.Status);
            Assert.Equal(PaymentSimulator.RejectedReason, result.FailureReason);
        }

        [Fact]
        public async Task Charge_ImporteTerminadoEn33_DevuelveUnavailable()
        {
            var result = await _service.ChargeAsync(Request("op-3", 1033));

            Assert.Equal(PaymentStatus.UNAVAILABLE, result.Status);
            Assert.Equal(PaymentSimulator.UnavailableReason, result.FailureReason);
        }

        [Fact]
        public async Task Charge_MismoOperationIdDosVeces_NoDuplicaYDevuelveElMismoPago()
        {
            var first = await _service.ChargeAsync(Request("op-4"));
            var retry = await _service.ChargeAsync(Request("op-4"));   

            Assert.Equal(1, _repo.Count);
            Assert.Equal(first.PaymentId, retry.PaymentId);
            Assert.Equal(first.Status, retry.Status);
        }

        [Fact]
        public async Task Charge_MismoOperationIdConOtroImporte_LanzaConflicto()
        {
            await _service.ChargeAsync(Request("op-5", 1000));

            await Assert.ThrowsAsync<PaymentConflictException>(
                () => _service.ChargeAsync(Request("op-5", 2000)));
        }

        [Fact]
        public async Task Charge_PagoUnavailableAnterior_SeReintenta()
        {
            _repo.Seed(Seeded("op-6", PaymentStatus.UNAVAILABLE, DateTime.UtcNow));

            var result = await _service.ChargeAsync(Request("op-6", 1000));

            Assert.Equal(PaymentStatus.SUCCEEDED, result.Status);
        }

        [Fact]
        public async Task Charge_PendingHuerfano_SeReprocesa()
        {
            _repo.Seed(Seeded("op-7", PaymentStatus.PENDING, DateTime.UtcNow.AddMinutes(-2)));

            var result = await _service.ChargeAsync(Request("op-7", 1000));

            Assert.Equal(PaymentStatus.SUCCEEDED, result.Status);
        }

        [Fact]
        public async Task Charge_PendingReciente_DevuelvePendingSinReprocesar()
        {
            _repo.Seed(Seeded("op-8", PaymentStatus.PENDING, DateTime.UtcNow));

            var result = await _service.ChargeAsync(Request("op-8", 1000));

            Assert.Equal(PaymentStatus.PENDING, result.Status);
        }

        [Fact]
        public async Task GetPayment_OperacionInexistente_DevuelveNull()
        {
            var result = await _service.GetPaymentAsync("no-existe");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetPayment_OperacionExistente_DevuelveElResultadoSinCobrarDeNuevo()
        {
            var charged = await _service.ChargeAsync(Request("op-9", 1099));

            var result = await _service.GetPaymentAsync("op-9");

            Assert.NotNull(result);
            Assert.Equal(charged.PaymentId, result!.PaymentId);
            Assert.Equal(PaymentStatus.REJECTED, result.Status);
            Assert.Equal(1, _repo.Count);
        }

        [Fact]
        public async Task Charge_CarreraConDatosDistintos_LanzaConflicto()
        {
            // Otro request gana la carrera con el mismo OperationId pero otro importe.
            _repo.RaceWinner = Seeded("op-10", PaymentStatus.PENDING, DateTime.UtcNow);   // importe 1000

            await Assert.ThrowsAsync<PaymentConflictException>(
                () => _service.ChargeAsync(Request("op-10", 2000)));
        }

        [Fact]
        public async Task Charge_CarreraConMismosDatos_DevuelveElPagoDelGanador()
        {
            var winner = Seeded("op-11", PaymentStatus.PENDING, DateTime.UtcNow);
            _repo.RaceWinner = winner;

            var result = await _service.ChargeAsync(Request("op-11", 1000));

            Assert.Equal(winner.Id.ToString(), result.PaymentId);
            Assert.Equal(PaymentStatus.PENDING, result.Status);
            Assert.Equal(1, _repo.Count);
        }

        [Fact]
        public async Task Charge_TryAddFalloPeroNoHayRegistro_LanzaErrorClaro()
        {
            _repo.ForceAddToFail = true;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _service.ChargeAsync(Request("op-12", 1000)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-500)]
        public async Task Charge_ImporteInvalido_LanzaValidacion_YNoGuarda(long amountCents)
        {
            await Assert.ThrowsAsync<PaymentValidationException>(
                () => _service.ChargeAsync(Request("op-v1", amountCents)));

            Assert.Equal(0, _repo.Count);
        }

        [Theory]
        [InlineData("ars")]
        [InlineData("AR")]
        [InlineData("")]
        public async Task Charge_MonedaInvalida_LanzaValidacion(string currency)
        {
            var request = Request("op-v2");
            request.Currency = currency;

            await Assert.ThrowsAsync<PaymentValidationException>(() => _service.ChargeAsync(request));
        }

        [Fact]
        public async Task Charge_OperationIdVacio_LanzaValidacion()
        {
            await Assert.ThrowsAsync<PaymentValidationException>(() => _service.ChargeAsync(Request("")));
        }

        private static ChargeRequestDto Request(string operationId, long amountCents = 1000) => new()
        {
            ReservationId = "res-1",
            OperationId = operationId,
            AmountCents = amountCents,
            Currency = "ARS"
        };

        private static PaymentRecord Seeded (string operationId, PaymentStatus status, DateTime createdAt) => new()
        {
            Id = Guid.NewGuid(),
            ReservationId = "res-1",
            OperationId = operationId,
            AmountCents = 1000,          
            Currency = "ARS",
            Status = status,
            CreatedAt = createdAt
        };
    }
}
