using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SeatReservation.Contracts.Payment;

namespace Booking.Api.Payments;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentCommunication(
        this IServiceCollection services, IConfiguration configuration)
    {
        // La configuración se valida al resolver el gateway. No se contacta con
        // Keycloak ni Payment al iniciar la API o consultar sus endpoints actuales.
        services.AddOptions<PaymentClientOptions>()
            .Bind(configuration.GetSection(PaymentClientOptions.SectionName))
            .Validate(o => IsHttpAddress(o.Address), "Payment:Address debe ser una URL HTTP o HTTPS absoluta.")
            .Validate(o => IsHttpAddress(o.TokenEndpoint), "Payment:TokenEndpoint debe ser una URL HTTP o HTTPS absoluta.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "Falta Payment:ClientId.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "Falta Payment:ClientSecret.")
            .Validate(o => o.DeadlineSeconds > 0, "Payment:DeadlineSeconds debe ser mayor que cero.")
            .Validate(o => o.TokenTimeoutSeconds > 0, "Payment:TokenTimeoutSeconds debe ser mayor que cero.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<PaymentTokenProvider>((sp, client) =>
            client.Timeout = TimeSpan.FromSeconds(
                sp.GetRequiredService<IOptions<PaymentClientOptions>>().Value.TokenTimeoutSeconds));

        services.AddGrpcClient<PaymentService.PaymentServiceClient>((sp, client) =>
            client.Address = new Uri(sp.GetRequiredService<IOptions<PaymentClientOptions>>().Value.Address))
            .ConfigureChannel(channel => channel.ThrowOperationCanceledOnCancellation = true);
        services.AddTransient<GrpcPaymentGateway>();
        return services;
    }

    private static bool IsHttpAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
