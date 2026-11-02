
namespace Payment.Data.Entities
{
    public record PaymentRecord
    {
        public Guid id { get; init; }
        public string reservationId { get; set; }
        public string OperationId { get; set; }
        public Int64 amoutCents { get; set; }
        public string currency { get; set; }
        public PaymentStatus status { get; set; }
        public string? failureReason { get; set; }

    }
}
