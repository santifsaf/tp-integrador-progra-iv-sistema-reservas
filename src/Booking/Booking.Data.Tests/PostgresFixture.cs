using Booking.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Booking.Data.Tests;

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string _connectionString;
    private DbContextOptions<BookingDbContext>? _options;

    public PostgresFixture()
    {
        _connectionString = Environment.GetEnvironmentVariable("BOOKING_TEST_DB")
            ?? "Server=localhost;Port=5432;Database=booking_tests;User Id=booking;Password=booking;";
    }

    public async Task InitializeAsync()
    {
        // Protección: verifica que sea la base de pruebas, no sea que borremos la de desarrollo.
        var connectionBuilder = new Npgsql.NpgsqlConnectionStringBuilder(_connectionString);
        var dbName = connectionBuilder.Database ?? "";
        if (!dbName.EndsWith("_tests", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"La base de datos '{dbName}' no termina en '_tests'. " +
                "Esto es una protección para evitar truncar la base de desarrollo por accidente.");
        }

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        _options = options;

        // Aplicar migraciones: también valida que la migración sea correcta.
        await using var context = new BookingDbContext(options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await ResetAsync();
    }

    public BookingDbContext CreateContext()
    {
        if (_options is null)
        {
            throw new InvalidOperationException("Fixture no inicializado. Llama a InitializeAsync primero.");
        }

        return new BookingDbContext(_options);
    }

    public async Task ResetAsync()
    {
        if (_options is null)
        {
            return;
        }

        await using var context = new BookingDbContext(_options);
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE reservations");
    }
}
