using Booking.Business.Events;
using Booking.Business.Services;
using Booking.Business.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Booking.Business;

public static class DependencyInjection
{
    // Registra la lógica de procesamiento de reservas y el job de vencimiento.
    // Requiere que ya estén registrados IReservationRepository (Luis) e IPaymentClient (Santi).
    public static IServiceCollection AddReservationProcessing(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ReservationsOptions>(configuration.GetSection(ReservationsOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);

        // TryAdd: si Juan y Kevin registran antes un publicador real, se usa ese.
        services.TryAddScoped<IReservationEventPublisher, LoggingReservationEventPublisher>();

        services.AddScoped<ReservationRequestProcessor>();
        services.AddScoped<ReservationExpirationService>();
        services.AddHostedService<ReservationExpirationWorker>();

        return services;
    }
}
