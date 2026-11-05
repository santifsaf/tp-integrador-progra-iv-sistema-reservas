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
            .Bind(configuration.GetSection("ServiceBus"))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ConnectionString) && !string.IsNullOrWhiteSpace(o.TopicName),
                "Falta ServiceBus:ConnectionString o ServiceBus:TopicName. Local: dotnet user-secrets set \"ServiceBus:ConnectionString\" \"...\" --project src/Availability/Availability.Api. Docker: completar MSG_BROKER_CONNECTION_STRING en infra/.env. Ver infra/README_AVAILABILITY.md.")
            .ValidateOnStart();
        services.AddOptions<BookingClientOptions>()
            .Bind(configuration.GetSection("Booking"))
            .Validate(
                o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                "Falta Booking:BaseUrl o no es una URL absoluta. Configurarla en appsettings o con la variable de entorno Booking__BaseUrl.")
            .ValidateOnStart();
        services.AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection("Keycloak"))
            .Validate(
                o => Uri.TryCreate(o.TokenEndpoint, UriKind.Absolute, out _)
                    && !string.IsNullOrWhiteSpace(o.ClientId)
                    && !string.IsNullOrWhiteSpace(o.ClientSecret),
                "Falta Keycloak:TokenEndpoint, Keycloak:ClientId o Keycloak:ClientSecret. Configurarlas en appsettings, o con las variables de entorno Keycloak__*.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdHandler>();
        services.AddTransient<BookingAuthHandler>();

        var availabilityDbConnectionString = configuration.GetConnectionString("AvailabilityDb");
        if (string.IsNullOrWhiteSpace(availabilityDbConnectionString))
        {
            throw new InvalidOperationException(
                "Falta ConnectionStrings:AvailabilityDb. Configurarla en appsettings.Development.json o con la variable de entorno ConnectionStrings__AvailabilityDb.");
        }

        services.AddDbContext<AvailabilityDbContext>(options =>
            options.UseNpgsql(availabilityDbConnectionString));

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
