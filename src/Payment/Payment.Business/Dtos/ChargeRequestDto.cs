
namespace Payment.Business.Dtos
{
    public class ChargeRequestDto
    {
        public string ReservationId { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public Int64 AmountCents { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
}
