using Availability.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Availability.Infrastructure.Persistence.Configurations;

public sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    private static readonly Guid EventOneId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EventTwoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("Seats");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Label).HasMaxLength(20).IsRequired();
        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.EventId);

        builder.HasData(
            new Seat { Id = Guid.Parse("11111111-1111-1111-1111-111111111101"), EventId = EventOneId, Label = "A1" },
            new Seat { Id = Guid.Parse("11111111-1111-1111-1111-111111111102"), EventId = EventOneId, Label = "A2" },
            new Seat { Id = Guid.Parse("11111111-1111-1111-1111-111111111103"), EventId = EventOneId, Label = "B1" },
            new Seat { Id = Guid.Parse("11111111-1111-1111-1111-111111111104"), EventId = EventOneId, Label = "B2" },
            new Seat { Id = Guid.Parse("22222222-2222-2222-2222-222222222201"), EventId = EventTwoId, Label = "A1" },
            new Seat { Id = Guid.Parse("22222222-2222-2222-2222-222222222202"), EventId = EventTwoId, Label = "A2" },
            new Seat { Id = Guid.Parse("22222222-2222-2222-2222-222222222203"), EventId = EventTwoId, Label = "B1" },
            new Seat { Id = Guid.Parse("22222222-2222-2222-2222-222222222204"), EventId = EventTwoId, Label = "B2" });
    }
}
