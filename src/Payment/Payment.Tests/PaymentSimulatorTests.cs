
using Payment.Business.Services;
using Payment.Data.Entities;
using Xunit;

namespace Payment.Tests
{
    public class PaymentSimulatorTests
    {
        private readonly PaymentSimulator _simulator = new();

        [Theory]
        [InlineData(1000)]
        [InlineData(1500050)]
        [InlineData(1)]

        //CASOS DE EXITO: cualquier monto que no termine en 99 o 33
        public void Simulate_ReturnSucceeded(long amountCents)
        {
            var (Status, FailureReason) = _simulator.Simulate(amountCents);

            Assert.Equal(PaymentStatus.SUCCEEDED, Status);
            Assert.Null(FailureReason);
        }

        [Theory]
        [InlineData(1099)]
        [InlineData(99)]
        [InlineData(1500099)]

        //CASOS DE RECHAZO: cualquier monto que termine en 99
        public void Simulate_ReturnsRejected(long amountCents)
        {
            var (Status, FailureReason) = _simulator.Simulate(amountCents);

            Assert.Equal(PaymentStatus.REJECTED, Status);
            Assert.Equal(PaymentSimulator.RejectedReason, FailureReason);
        }

        //CASOS DE NO DISPONIBLE: cualquier monto que termine en 33
        [Theory]
        [InlineData(1033)]
        [InlineData(33)]
        [InlineData(1500033)]
        public void Simulate_ReturnsUnavailable(long amountCents)
        {
            var (Status, FailureReason) = _simulator.Simulate(amountCents);

            Assert.Equal(PaymentStatus.UNAVAILABLE, Status);
            Assert.Equal(PaymentSimulator.UnavailableReason, FailureReason);
        }
    }
}
