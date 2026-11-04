using Availability.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Availability.Infrastructure;

public static class DatabaseInitializer
{
    public static async Task ApplyMigrationsAsync(
        this IServiceProvider services,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AvailabilityDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
