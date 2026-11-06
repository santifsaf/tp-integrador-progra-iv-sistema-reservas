
using Payment.Business.Dtos;

namespace Payment.Business.Services
{
    public interface IPaymentService
    {
        Task<PaymentResultDto> ChargeAsync(ChargeRequestDto request);

        Task<PaymentResultDto?> GetPaymentAsync(string operationId); // devuelve null si la operacion no existe
    }
}
