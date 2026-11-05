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

    // Aplica las migraciones pendientes. Si no se registró AddBookingData (por ejemplo, en el
    // entorno Testing sin connection string), no hace nada. Con varias réplicas es seguro: EF
    // toma un bloqueo exclusivo al migrar y las demás esperan.
    public static async Task MigrateBookingDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetService<BookingDbContext>();
        if (db is null)
        {
            return;
        }

        await db.Database.MigrateAsync(ct);
    }
}