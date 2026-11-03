namespace Booking.Api.Payments;

public sealed class PaymentClientOptions
{
    public const string SectionName = "Payment";

    public string Address { get; set; } = "";
    public string TokenEndpoint { get; set; } = "";
    public string ClientId { get; set; } = "booking";
    // Se proporciona mediante Payment__ClientSecret, no desde el repositorio.
    public string ClientSecret { get; set; } = "";
    public int DeadlineSeconds { get; set; } = 5;
    public int TokenTimeoutSeconds { get; set; } = 5;
}
