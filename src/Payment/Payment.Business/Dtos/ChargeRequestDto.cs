
namespace Payment.Business.Dtos
{
    public class ChargeRequestDto
    {
        public string reservationId { get; set; }
        public string operationId { get; set; }
        public Int64 amountCents { get; set; }
        public string currency { get; set; }
    }
}
