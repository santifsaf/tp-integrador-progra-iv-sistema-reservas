
using Microsoft.EntityFrameworkCore;
using Payment.Data.Entities;

namespace Payment.Data
{
    // Base propia de Payment (PostgreSQL), cada microservicio tiene su propia base de datos
    // Ningun otro servicio lee esta base. Si booking requiere de datos de pago, lo pide por medio de gRPC
    public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
    {
        public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PaymentRecord>(e =>
            {
                e.ToTable("payments");
                e.HasKey(p => p.Id);
                // OperationId unico para lograr idempotencia de la operacion de pago
                e.HasIndex(p => p.OperationId).IsUnique(); 
                e.Property(p => p.OperationId).IsRequired().HasMaxLength(100);
                e.Property(p => p.ReservationId).IsRequired().HasMaxLength(100);
                e.Property(p => p.Currency).IsRequired().HasMaxLength(3);
                e.Property(p => p.FailureReason).HasMaxLength(300);
                // Status se guarda como string en la base de datos para su legibilidad y no depende de la posicion del enum
                e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20); 
            });
        }


    }
}
