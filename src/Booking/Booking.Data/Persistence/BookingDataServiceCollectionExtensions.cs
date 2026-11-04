using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Data.Persistence;

public static class BookingDataServiceCollectionExtensions
{
    // Recibe el string (no IConfiguration) para que Booking.Data no dependa de paquetes de configuración.
    public static IServiceCollection AddBookingData(this IServiceCollection services, string connectionString)
    {
        // Scoped: DbContext no es thread-safe, así que cada request (o cada mensaje) tiene el suyo.
        services.AddDbContext<BookingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IReservationRepository, ReservationRepository>();
        return services;
    }
}