using Availability.Application.Abstractions;
using Availability.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Availability.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GetEventsUseCase>();
        services.AddScoped<GetSeatMapUseCase>();
        services.AddScoped<IGetSeatsAvailabilityUseCase, GetSeatsAvailabilityUseCase>();
        services.AddScoped<ICreateReservationRequestUseCase, CreateReservationRequestUseCase>();
        services.AddScoped<IGetReservationStatusUseCase, GetRequestStatusUseCase>();
        services.AddScoped<GetUserRequestsUseCase>();

        return services;
    }
}
