
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payment.Data.Entities;

namespace Payment.Data.Repositories
{
    public class PaymentRepository(PaymentDbContext db) : IPaymentRepository

    {
        private readonly PaymentDbContext _db = db;

        public Task<PaymentRecord?> GetByOperationIdAsync(string operationId, CancellationToken ct = default)
            => _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OperationId == operationId, ct);

        public async Task<bool> TryAddAsync(PaymentRecord payment, CancellationToken ct = default)
        {
            _db.Payments.Add(payment);
            try
            {
                await _db.SaveChangesAsync(ct);
                return true;
            }

            //Solo entramos al catch si el error es por violacion de unicidad y es por el OperationId 
            catch (DbUpdateException e) when (e.InnerException is PostgresException 
            { 
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: PaymentDbContext.OperationIdIndexName
            })
            {
                _db.Entry(payment).State = EntityState.Detached;
                return false;
            }
        }

        public async Task UpdateAsync(PaymentRecord payment, CancellationToken ct = default)
        {
            _db.Payments.Update(payment);
            await _db.SaveChangesAsync(ct);
        }
    }
}
