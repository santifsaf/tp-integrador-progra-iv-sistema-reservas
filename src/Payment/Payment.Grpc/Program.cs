var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
var app = builder.Build();
// Registrar los servicios con MapGrpcService<T> cuando se acuerde el contrato de pago.
app.MapGet("/health", () => Results.Ok(new { service = "Payment", status = "ok" }));
app.Run();
