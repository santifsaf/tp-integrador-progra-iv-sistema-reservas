using Microsoft.EntityFrameworkCore;
using Payment.Business.Services;
using Payment.Data;
using Payment.Data.Repositories;
using Payment.Grpc.Auditing;
using Payment.Grpc.Security;
using Payment.Grpc.Services;

// PATRÓN (consigna 3c): Payment es N-Layer. Este proyecto es la capa de presentación
// (gRPC): sólo configura el host, la seguridad y los interceptores. La lógica de cobro
// vive en Payment.Business y el acceso a datos en Payment.Data; el dominio es anémico
// (entidades con datos, reglas en los servicios), como pide el enunciado.
var builder = WebApplication.CreateBuilder(args);

// Configurar la conexión a la base de datos de Payment.
var connectionString = builder.Configuration.GetConnectionString("Payment")
    ?? throw new InvalidOperationException(
        "Falta configurar ConnectionStrings:Payment.");

// Registrar Entity Framework Core con PostgreSQL.
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(connectionString));

// Registrar el repositorio de pagos.
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();

// Registrar la lógica de negocio y el simulador.
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddSingleton<PaymentSimulator>();

// Consigna 3e: autenticación de microservicios contra Keycloak.
// El FallbackPolicy de AddPaymentAuthentication protege cualquier gRPC que se mapee.
builder.Services.AddPaymentAuthentication(builder.Configuration);

// El interceptor se registra en DI y se aplica a todos los servicios gRPC.
builder.Services.AddSingleton<GrpcAuditInterceptor>();
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcAuditInterceptor>();
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// /health queda fuera de la autenticación para que Docker y los smoke tests puedan
// consultarlo sin token. No expone información de negocio.
app.MapGet("/health", () => Results.Ok(new { service = "Payment", status = "ok" }))
    .AllowAnonymous();

// Registrar el servicio gRPC de Payment.
// La autenticación y autorización se aplican mediante la configuración de Kevin.
app.MapGrpcService<PaymentGrpcService>();

app.Run();
