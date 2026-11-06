
using Payment.Data.Entities;
using Payment.Data.Repositories;

namespace Payment.Tests.Helpers
{
    //Repositorio falso para probar la logica de PaymentService sin una base de datos.
    //Se guardan pagos en memoria, cada lectura devuelve un objeto nuevo para simular que EF devuelve entidades nuevas cada vez.
    public sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly Dictionary<string, PaymentRecord> _store = [];
        
        // Simula que otra request gano la carrera pero se devuelve false en TryAddAsync
        public PaymentRecord? RaceWinner { get; set; } 

        //Fuerza que TryAddAsync devuelva false sin insertar nada (caso especial)
        public bool ForceAddToFail { get; set; }

        public int Count => _store.Count;

        // Setear un estado inicial (por ejemplo, un Pending viejo).
        public void Seed(PaymentRecord payment) => _store[payment.OperationId] = Clone(payment);

        //Si encontro el pago, devuelve una copia nueva sino null.
        public Task<PaymentRecord?> GetByOperationIdAsync(string operationId, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(operationId, out var p) ? Clone(p) : null);

        //Verifica si ya existe un pago con el mismo OperationId, si no existe lo agrega y devuelve true, sino devuelve false.
        public Task<bool> TryAddAsync(PaymentRecord payment, CancellationToken ct = default)
        {
            if (RaceWinner is not null)
            {
                _store[RaceWinner.OperationId] = Clone(RaceWinner);
                RaceWinner = null;
                return Task.FromResult(false);
            }

            if (ForceAddToFail) return Task.FromResult(false);

            if (_store.ContainsKey(payment.OperationId)) return Task.FromResult(false);
            _store[payment.OperationId] = Clone(payment);
            return Task.FromResult(true);
        }

        // Reemplaza la copia existente por la nueva copia del pago actualizado.
        public Task UpdateAsync(PaymentRecord payment, CancellationToken ct = default)
        {
            _store[payment.OperationId] = Clone(payment);
            return Task.CompletedTask;
        }

        private static PaymentRecord Clone(PaymentRecord p) => new()
        {
            Id = p.Id,
            ReservationId = p.ReservationId,
            OperationId = p.OperationId,
            AmountCents = p.AmountCents,
            Currency = p.Currency,
            Status = p.Status,
            FailureReason = p.FailureReason,
            CreatedAt = p.CreatedAt,
            CompletedAt = p.CompletedAt
        };
    }
}
