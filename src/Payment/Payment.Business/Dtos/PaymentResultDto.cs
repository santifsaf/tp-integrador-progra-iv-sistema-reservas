
using Payment.Data.Entities;

namespace Payment.Business.Dtos
{
    public class PaymentResultDto
    {
        public string paymentId { get; set; }
        public PaymentStatus status { get; set; }
        public string? failureReason { get; set; }
    }
}
