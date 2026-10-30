using Availability.Api.Middleware;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configurar Serilog como proveedor de logging
builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddSerilog();
});

// Configurar Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/availability-api-.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Availability.Api")
    .CreateLogger();

try
{
    Log.Information("Iniciando aplicación Availability.Api");

    builder.Services.AddControllers();
    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();

    app.MapControllers();
    app.MapGet("/health", () => Results.Ok(new { service = "Availability", status = "ok" }));

    Log.Information("Aplicación configurada. Iniciando servidor...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicación terminó inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}
