using Availability.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Availability.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GetEventsUseCase>();
        services.AddScoped<GetSeatMapUseCase>();
        services.AddScoped<CreateReservationRequestUseCase>();
        services.AddScoped<GetRequestStatusUseCase>();
        services.AddScoped<GetUserRequestsUseCase>();

        return services;
    }
}
