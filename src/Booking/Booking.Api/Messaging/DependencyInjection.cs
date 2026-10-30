using Azure.Messaging.ServiceBus;
using Booking.Business;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Booking.Api.Messaging;

public static class DependencyInjection
{
    // Única línea que hace falta en el Program.cs de Booking.Api:
    //
    //   builder.Services.AddReservationRequestConsumer(builder.Configuration);
    //
    // Antes de esa línea tienen que estar registrados IReservationRepository e IPaymentClient.
    public static IServiceCollection AddReservationRequestConsumer(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddReservationProcessing(configuration);

        services.Configure<BrokerOptions>(configuration.GetSection(BrokerOptions.SectionName));

        // Un solo ServiceBusClient por proceso: es thread-safe y mantiene la conexión
        // abierta (lo recomienda el SDK de Azure).
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BrokerOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    "Falta Broker:ConnectionString. Pasarla con la variable de entorno Broker__ConnectionString.");
            }

            return new ServiceBusClient(options.ConnectionString);
        });

        services.AddHostedService<ReservationRequestedConsumer>();

        return services;
    }
}
