using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Booking.Api.Security;

public static class BookingAuthentication
{
    public const string AvailabilityPolicy = "AvailabilityService";

    public static IServiceCollection AddBookingAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("Authentication");
        var authority = section["Authority"]
            ?? throw new InvalidOperationException("Falta Authentication:Authority.");
        var audience = section["Audience"]
            ?? throw new InvalidOperationException("Falta Authentication:Audience.");
        var allowedClient = section["AllowedClientId"]
            ?? throw new InvalidOperationException("Falta Authentication:AllowedClientId.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.MapInboundClaims = false;
                // La configuración local de Keycloak usa HTTP. Fuera de Development se
                // exige HTTPS para obtener la metadata y las claves de validación.
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                if (!string.IsNullOrWhiteSpace(section["MetadataAddress"]))
                    options.MetadataAddress = section["MetadataAddress"]!;

                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidIssuer = authority;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            });

        services.AddAuthorization(options =>
            options.AddPolicy(AvailabilityPolicy, policy => policy
                .RequireAuthenticatedUser()
                // azp identifica al cliente de Keycloak. El sub del token de
                // client credentials identifica la cuenta del servicio, no al usuario.
                .RequireClaim("azp", allowedClient)));

        return services;
    }
}
