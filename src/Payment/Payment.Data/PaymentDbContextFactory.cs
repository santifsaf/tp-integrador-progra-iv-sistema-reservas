
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payment.Data
{
    //Fabrica usada solo por las herramientas de EF (dotnet ef) para generar migraciones
    //Asi Payment.Data genera sus migraciones sin depender del Program.cs de Payment.Grpc
    //La cadena real del servicio se configura en Payment.Grpc
    public class PaymentDbContextFactory: IDesignTimeDbContextFactory<PaymentDbContext>
    {
        public PaymentDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<PaymentDbContext>()
                .UseNpgsql("Host=localhost;Port=5432;Database=payment;Username=postgres;Password=postgres")
                .Options;

            return new PaymentDbContext(options);
        }
    }
}
