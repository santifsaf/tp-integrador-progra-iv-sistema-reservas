
namespace Payment.Business.Exceptions
{
    public class PaymentConflictException : Exception
    {
        public PaymentConflictException(string operationId)
            : base($"La operacion {operationId} ya existe con datos modificados") { }
        //se lanza si llega una operacion con el mismo operationId pero con datos distintos a los originales
    }
}
