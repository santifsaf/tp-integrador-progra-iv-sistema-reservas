
using Payment.Data.Entities;

namespace Payment.Business.Services
{
    /* 
     Simulador determinista para que las pruebas sean reproducibles.
    Estas separado de PaymentService por principio de responsabilidad unica, si el dia de mañana se reemplaza por una 
    pasarela real, solo se cambia esta clase y no PaymentService.
     */
    public class PaymentSimulator
    {
        public const string RejectedReason = "Pago Fallido";
        public const string UnavailableReason = "Pago no disponible";
        public (PaymentStatus Status, string? FailureReason) Simulate (long amountCents)
        {
            var lastTwoDigits = amountCents % 100;

            return lastTwoDigits switch
            {
                99 => (PaymentStatus.REJECTED, RejectedReason),
                33 => (PaymentStatus.UNAVAILABLE, UnavailableReason),
                _ => (PaymentStatus.SUCCEEDED, null)
            };
        }
    }
}
