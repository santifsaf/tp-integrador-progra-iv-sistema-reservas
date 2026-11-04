
namespace Payment.Data.Entities
{
    public class PaymentRecord
    {
        public Guid Id { get; init; }
        public string ReservationId { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public Int64 AmountCents { get; set; }
        public string Currency { get; set; } = string.Empty;
        public PaymentStatus Status { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
