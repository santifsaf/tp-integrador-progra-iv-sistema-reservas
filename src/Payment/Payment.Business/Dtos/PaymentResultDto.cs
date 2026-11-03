
using Payment.Data.Entities;

namespace Payment.Business.Dtos
{
    public class PaymentResultDto
    {
        public string PaymentId { get; set; } = string.Empty;
        public PaymentStatus Status { get; set; }
        public string? FailureReason { get; set; }
    }
}
