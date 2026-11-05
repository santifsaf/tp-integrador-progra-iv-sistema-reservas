using Booking.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Booking.Data.Tests;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class BookingDatabaseMigrationTests
{
    private readonly PostgresFixture _fixture;

    public BookingDatabaseMigrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SinAddBookingData_MigrateNoLanzaExcepcion()
    {
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();

        // Sin AddBookingData registrado, GetService<BookingDbContext> devuelve null y
        // MigrateBookingDatabaseAsync no hace nada. No debe fallar.
        await provider.MigrateBookingDatabaseAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConAddBookingData_MigrateEsIdempotente()
    {
        var services = new ServiceCollection();
        services.AddBookingData(_fixture.ConnectionString);
        await using var provider = services.BuildServiceProvider();

        // Primera llamada: aplica las migraciones pendientes.
        await provider.MigrateBookingDatabaseAsync(CancellationToken.None);

        // Segunda llamada: no debe fallar, ya que todas están aplicadas.
        await provider.MigrateBookingDatabaseAsync(CancellationToken.None);

        // Verificar que no hay migraciones pendientes y las aplicadas coinciden con todas.
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        var allMigrations = db.Database.GetMigrations();

        Assert.Empty(pendingMigrations);
        Assert.Equal(allMigrations.OrderBy(m => m), appliedMigrations.OrderBy(m => m));
    }

    [Fact]
    public async Task BaseVacia_MigrateCreaElEsquemaCompleto()
    {
        // Base descartable solo para este test: no toca booking_tests ni sus datos.
        // El sufijo _tests es obligatorio por el seguro anti-desastres de la fixture.
        var scratchName = $"booking_migrate_{Guid.NewGuid():N}_tests";

        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{scratchName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var scratchConnectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
            {
                Database = scratchName
            }.ConnectionString;

            await using var provider = new ServiceCollection()
                .AddBookingData(scratchConnectionString)
                .BuildServiceProvider();

            await provider.MigrateBookingDatabaseAsync(CancellationToken.None);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(0, await db.Reservations.CountAsync());   // la tabla existe y está vacía
        }
        finally
        {
            // Se cierran las conexiones del pool para poder borrar la base.
            NpgsqlConnection.ClearAllPools();

            await using var admin = new NpgsqlConnection(_fixture.ConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{scratchName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
