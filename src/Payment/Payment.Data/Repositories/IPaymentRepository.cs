
using Payment.Data.Entities;

namespace Payment.Data.Repositories
{
    // Uso del patron Repository, ya que business no usa el DbContext directamente sino por esta interfaz.
    // La logica de negocio no depende de Entity Framework ni de Postgres.
    public interface IPaymentRepository
    {
        Task<PaymentRecord?> GetByOperationIdAsync(string operationId, CancellationToken ct = default);

        Task<bool> TryAddAsync(PaymentRecord payment, CancellationToken ct = default); // devuelve false si existe un pago con el mismo OperationId

        Task UpdateAsync(PaymentRecord payment, CancellationToken ct = default);
    }
}
