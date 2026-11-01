using Availability.Api.Middleware;
using Availability.Application.Abstractions;
using Availability.Application.UseCases;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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
        path: "logs/availability-api-{Date}.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Availability.Api")
    .CreateLogger();

try
{
    Log.Information("Iniciando aplicación Availability.Api");

    // Obtener configuración de JWT desde appsettings.json
    var jwtSettings = builder.Configuration.GetSection("JwtSettings");

    builder.Services.AddControllers();

    // Registrar casos de uso y dependencias de aplicación
    builder.Services.AddScoped<GetSeatMapUseCase>();
    builder.Services.AddScoped<IGetSeatsAvailabilityUseCase, GetSeatsAvailabilityUseCase>();
    builder.Services.AddScoped<ICreateReservationRequestUseCase, CreateReservationRequestUseCase>();
    builder.Services.AddScoped<IGetReservationStatusUseCase, GetRequestStatusUseCase>();

    // Configurar autenticación JWT Bearer
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.Authority = jwtSettings["Authority"];
            options.Audience = jwtSettings["Audience"];
            options.RequireHttpsMetadata = bool.Parse(jwtSettings["RequireHttpsMetadata"] ?? "true");
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = bool.Parse(jwtSettings["ValidateIssuerSigningKey"] ?? "true"),
                ValidateLifetime = bool.Parse(jwtSettings["ValidateLifetime"] ?? "true")
            };
        });

    // Configurar autorización
    builder.Services.AddAuthorization();

    var app = builder.Build();

    // Orden crítico de middleware:
    // 1. ExceptionHandlingMiddleware PRIMERO para capturar excepciones de TODO lo que viene después
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // 2. CorrelationIdMiddleware SEGUNDO para generar IDs de correlación antes de autenticación
    app.UseMiddleware<CorrelationIdMiddleware>();

    // 3. Autenticación
    app.UseAuthentication();

    // 4. Autorización
    app.UseAuthorization();

    // Mapear controladores
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
