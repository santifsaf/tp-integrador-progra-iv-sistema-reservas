using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Availability.Infrastructure.Persistence;

public sealed class AvailabilityDbContext(DbContextOptions<AvailabilityDbContext> options) : DbContext(options)
{
    public DbSet<ReservationRequest> ReservationRequests => Set<ReservationRequest>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Seat> Seats => Set<Seat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Las configuraciones viven en Infrastructure para mantener el dominio sin referencias a EF Core.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AvailabilityDbContext).Assembly);
    }
}
