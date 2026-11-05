
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payment.Data
{
    //Fabrica usada solo por las herramientas de EF (dotnet ef) para generar migraciones
    //Asi Payment.Data genera sus migraciones sin depender del Program.cs de Payment.Grpc
    //La cadena real del servicio se configura en Payment.Grpc
    public class PaymentDbContextFactory: IDesignTimeDbContextFactory<PaymentDbContext>
    {
        public const string ConnectionStringVar = "PAYMENT_DB_CONNECTION_STRING";
        public PaymentDbContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVar);

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    $"Falta la variable de entorno {ConnectionStringVar}. " + 
                    "Definila con la cadena de conexion de la base de Payment antes de ejecutar dotnet ef"
                    );

            var options = new DbContextOptionsBuilder<PaymentDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            return new PaymentDbContext(options);
        }
    }
}
