using Availability.Application.Abstractions;
using Availability.Infrastructure.Http;
using Availability.Infrastructure.Messaging;
using Availability.Infrastructure.Options;
using Availability.Infrastructure.Persistence;
using Availability.Infrastructure.Repositories;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Availability.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ServiceBusOptions>()
            .Bind(configuration.GetSection("ServiceBus"));
        services.AddOptions<BookingClientOptions>()
            .Bind(configuration.GetSection("Booking"));
        services.AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection("Keycloak"));

        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdHandler>();
        services.AddTransient<BookingAuthHandler>();

        services.AddDbContext<AvailabilityDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("AvailabilityDb")));

        services.AddScoped<IReservationRequestRepository, ReservationRequestRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<ISeatRepository, SeatRepository>();
        services.AddScoped<IReservationPublisher, ServiceBusReservationPublisher>();
        services.AddScoped<IBookingClient, BookingClient>();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<ServiceBusOptions>>()
                .Value;
            return new ServiceBusClient(
                options.ConnectionString,
                new ServiceBusClientOptions
                {
                    RetryOptions = new ServiceBusRetryOptions()
                });
        });
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<ServiceBusOptions>>()
                .Value;
            return serviceProvider
                .GetRequiredService<ServiceBusClient>()
                .CreateSender(options.TopicName);
        });

        services.AddHttpClient(HttpClientNames.Keycloak)
            .AddStandardResilienceHandler();
        services.AddSingleton<KeycloakTokenProvider>();
        var bookingClient = services.AddHttpClient(HttpClientNames.Booking, (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<BookingClientOptions>>()
                    .Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            });
        bookingClient.AddStandardResilienceHandler();
        bookingClient.AddHttpMessageHandler<CorrelationIdHandler>();
        bookingClient.AddHttpMessageHandler<BookingAuthHandler>();

        return services;
    }
}

internal static class HttpClientNames
{
    public const string Keycloak = "Keycloak";
    public const string Booking = "Booking";
}
