using Booking.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Booking.Data.Persistence;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public const string ActiveSeatIndex = "ux_reservations_active_seat";
    public const string RequestIndex = "ux_reservations_request";
    public const string ExpirationIndex = "ix_reservations_expiration";

    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        // Mapeo a tabla en minúsculas
        builder.ToTable("reservations");

        // Clave primaria
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        // Propiedades básicas en snake_case
        builder.Property(r => r.RequestId)
            .HasColumnName("request_id")
            .IsRequired();

        builder.Property(r => r.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(r => r.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(r => r.SeatId)
            .HasColumnName("seat_id")
            .IsRequired();

        // Enums como texto
        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired(false);

        // Auditoría
        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired(false);

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Índices

        // Concurrencia: un solo Held/Confirmed por asiento
        builder.HasIndex(r => new { r.EventId, r.SeatId })
            .IsUnique()
            .HasDatabaseName(ActiveSeatIndex)
            .HasFilter("\"status\" IN ('Held', 'Confirmed')");

        // Idempotencia: una solicitud genera como máximo una reserva
        builder.HasIndex(r => r.RequestId)
            .IsUnique()
            .HasDatabaseName(RequestIndex);

        // Búsqueda rápida de Held vencidas
        builder.HasIndex(r => r.ExpiresAt)
            .HasDatabaseName(ExpirationIndex)
            .HasFilter("\"status\" = 'Held'");
    }
}
