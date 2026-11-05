using System.Reflection;
using Availability.Api.Middleware;
using Availability.Api.Swagger;
using Availability.Application;
using Microsoft.AspNetCore.Authentication;
using Availability.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
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

    // Obtener configuración de JWT desde appsettings.json o variables de entorno
    var jwtSettings = builder.Configuration.GetSection("JwtSettings");

    // En Docker, obtener valores de variables de entorno
    var authority = jwtSettings["Authority"] ?? builder.Configuration["JwtSettings:Authority"];
    var audience = jwtSettings["Audience"] ?? builder.Configuration["JwtSettings:Audience"];
    var requireHttpsMetadata = bool.Parse(jwtSettings["RequireHttpsMetadata"] ?? "true");

    builder.Services.AddControllers();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Availability API",
            Version = "v1",
            Description = "Consulta de eventos y asientos, y solicitudes de reserva."
        });

        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile), includeControllerXmlComments: true);

        options.AddSecurityDefinition(AuthorizeOperationFilter.SchemeName, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token de Keycloak (cliente web). Pegar solo el token, sin el prefijo Bearer."
        });
        options.OperationFilter<AuthorizeOperationFilter>();
    });

    // Registrar dependencias de la capa de aplicación
    builder.Services.AddApplication();

    // Registrar dependencias de la capa de infraestructura
    builder.Services.AddInfrastructure(builder.Configuration);
    // Configurar autenticación JWT Bearer
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.RequireHttpsMetadata = requireHttpsMetadata;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = bool.Parse(jwtSettings["ValidateIssuerSigningKey"] ?? "true"),
                ValidateLifetime = bool.Parse(jwtSettings["ValidateLifetime"] ?? "true")
            };
        });

    // Configurar autorización
    builder.Services.AddAuthorization();

    var app = builder.Build();

    // Aplicar migraciones al iniciar la aplicación
    await app.Services.ApplyMigrationsAsync();

    // Orden crítico de middleware:
    // 1. ExceptionHandlingMiddleware PRIMERO para capturar excepciones de TODO lo que viene después
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // 2. CorrelationIdMiddleware SEGUNDO para generar IDs de correlación antes de autenticación
    app.UseMiddleware<CorrelationIdMiddleware>();

    // Swagger: disponible en Development y Docker; nunca en Production
    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

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
