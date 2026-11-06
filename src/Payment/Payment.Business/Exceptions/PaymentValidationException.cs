
namespace Payment.Business.Exceptions
{
    public class PaymentValidationException : Exception
    {
        public PaymentValidationException(string message)
            : base(message) { }
    }
}
