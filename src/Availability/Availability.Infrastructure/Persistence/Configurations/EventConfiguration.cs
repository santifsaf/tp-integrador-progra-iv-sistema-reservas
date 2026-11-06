using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Availability.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    private static readonly Guid EventOneId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EventTwoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StartsAt).IsRequired();
        builder.Property(x => x.PriceCents).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();

        // GUID fijos y fechas UTC hacen que el seed sea reproducible entre migraciones.
        builder.HasData(
            new Event
            {
                Id = EventOneId,
                Name = "Festival de musica",
                StartsAt = new DateTime(2026, 12, 1, 20, 0, 0, DateTimeKind.Utc),
                PriceCents = 15000,
                Currency = "ARS"
            },
            new Event
            {
                Id = EventTwoId,
                Name = "Conferencia de tecnologia",
                StartsAt = new DateTime(2027, 1, 15, 18, 30, 0, DateTimeKind.Utc),
                PriceCents = 20000,
                Currency = "ARS"
            });
    }
}
