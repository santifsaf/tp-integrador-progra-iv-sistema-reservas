using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Booking.Data.Persistence;

// DECISIÓN: esta clase existe solo para que `dotnet ef` pueda crear el DbContext en
// tiempo de diseño (migraciones) sin arrancar Booking.Api. La app en ejecución no la usa.
// El valor por defecto apunta al Postgres local de desarrollo; no es un secreto real.
public sealed class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Booking")
            ?? "Host=localhost;Port=5432;Database=booking;Username=booking;Password=booking";

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BookingDbContext(options);
    }
}