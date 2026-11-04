
using Microsoft.Extensions.Logging;
using Payment.Business.Dtos;
using Payment.Business.Exceptions;
using Payment.Data.Entities;
using Payment.Data.Repositories;

namespace Payment.Business.Services
{
    /*
        Toda la logica de negocio de pagos se encuentra en esta clase.
        las entidads solo guardan datos (dominio anemico).
        Esta clase coordina al repositorio y al simulador de pagos para cumplir con los requerimientos de negocio, sin conocer gRPC ni EF
    */
    public class PaymentService(IPaymentRepository repository, PaymentSimulator simulator, ILogger<PaymentService> logger) : IPaymentService
    {
        private readonly IPaymentRepository _repository = repository;
        private readonly PaymentSimulator _simulator = simulator;
        private readonly ILogger<PaymentService> _logger = logger;

        // constante para definir cuanto tiempo se considera que un pago PENDING es "huerfano" y se puede reprocesar
        private static readonly TimeSpan StalePendingAfter = TimeSpan.FromSeconds(30);

        //Este metodo no recibe CancellationToken de la llamada gRPC, ya que la logica de negocio no tiene que conocer gRPC.
        //Si Booking supera su deadline, el pago debe completarse igual y quedar registrado
        //Para que GetPayment pueda consultar el resultado del pago despues.
        public async Task<PaymentResultDto> ChargeAsync(ChargeRequestDto request)
        {
            //Validaciones de negocio, si no se cumplen se lanza PaymentValidationException para no depender de gRPC.
            Validate(request);

            _logger.LogInformation("[{OperationId}] INICIO del cobro. Reserva = {ReservationId} Importe = {Amount} {Currency}",
                request.OperationId, request.ReservationId, request.AmountCents, request.Currency);

            var existing = await _repository.GetByOperationIdAsync(request.OperationId);

            if (existing is not null)
            {
                EnsureSameData(existing, request);

                var isStalePending = existing.Status == PaymentStatus.PENDING
                                     && DateTime.UtcNow - existing.CreatedAt > StalePendingAfter;

                // Se reprocesa si el pago estaba PENDING y ya paso mucho tiempo,
                // o si estaba UNAVAILABLE (simulador de pagos no estaba disponible)
                if (existing.Status == PaymentStatus.UNAVAILABLE || isStalePending)
                    return await ProcessAsync(existing);

                // Se devuelve lo guardado (SUCCEDED, REJECTED O un PENDING reciente)
                _logger.LogInformation("[{OperationId}] FIN del cobro. Estado={Status}",
                    request.OperationId, existing.Status);

                return ToDto(existing);
            }

            // Se guarda primero como PENDING, para que GetPayment pueda consultar el resultado del pago mientras se procesa.
            var payment = new PaymentRecord
            {
                Id = Guid.NewGuid(),
                ReservationId = request.ReservationId,
                OperationId = request.OperationId,
                AmountCents = request.AmountCents,
                Currency = request.Currency,
                Status = PaymentStatus.PENDING,
                CreatedAt = DateTime.UtcNow
            };

            if (!await _repository.TryAddAsync(payment))
            {
                //Dos request llegan casi al mismo tiempo y las dos intentan guardar un Payment 
                //La base solo deja pasar una y el perdedor busca el registro ganador y lo devuelve.
                var winner = await _repository.GetByOperationIdAsync(request.OperationId)
                    ?? throw new InvalidOperationException(
                        $"[{request.OperationId}] TryAddAsync indica que el OperationId ya existe pero no encontro el registro");


                EnsureSameData(winner, request);
                return ToDto(winner);
            }

            return await ProcessAsync(payment);

        }

        //Devuelve null si la operacion no existe, solo consulta
        public async Task<PaymentResultDto?> GetPaymentAsync(string operationId)
        {
            if(string.IsNullOrWhiteSpace(operationId))
                throw new PaymentValidationException("OperationId es obligatorio");

            var payment = await _repository.GetByOperationIdAsync(operationId);
            return payment is null ? null : ToDto(payment);
        }


        private static void EnsureSameData(PaymentRecord existing, ChargeRequestDto request)
        {
            if (existing.ReservationId != request.ReservationId
                || existing.AmountCents != request.AmountCents
                || existing.Currency != request.Currency)
                throw new PaymentConflictException(request.OperationId);
        }

        private static void Validate(ChargeRequestDto request)
        {
            if(string.IsNullOrWhiteSpace(request.ReservationId))
                throw new PaymentValidationException("ReservationId es obligatorio");

            if(string.IsNullOrWhiteSpace(request.OperationId))
                throw new PaymentValidationException("OperationId es obligatorio");

            if(request.AmountCents <= 0)
                throw new PaymentValidationException("AmountCents debe ser un valor positivo");

            if(request.Currency.Length != 3 || request.Currency.Any(c => c < 'A' || c > 'Z'))
                throw new PaymentValidationException("Currency debe tener 3 letras mayusculas");
        }

        //Simula el cobro, actualiza el estado y devuelve un resultado
        private async Task<PaymentResultDto> ProcessAsync(PaymentRecord payment)
        {
            var (Status, FailureReason) = _simulator.Simulate(payment.AmountCents);

            payment.Status = Status;
            payment.FailureReason = FailureReason;
            payment.CompletedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(payment);

            _logger.LogInformation("[{OperationId} FIN del cobro. Estado = {Status} Motivo = {FailureReason}]",
                payment.OperationId, payment.Status, payment.FailureReason);

            return ToDto(payment);
        }

        // Convierte la entidad a un DTO para devolverlo a Booking
        private static PaymentResultDto ToDto(PaymentRecord payment) => new()
        {
            PaymentId = payment.Id.ToString(),
            Status = payment.Status,
            FailureReason = payment.FailureReason
        };
    }
}
