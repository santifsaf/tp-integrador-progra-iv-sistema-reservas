using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Payment.Grpc.Security;

public static class AuthenticationExtensions
{
    // Autenticación de microservicios con Keycloak (consigna 3e).
    //
    // Flujo: Booking pide un token client_credentials con aud=payment, lo manda en la
    // metadata gRPC "authorization: Bearer <token>" y Payment valida:
    //   1. firma contra las claves públicas del realm (JWKS),
    //   2. issuer,
    //   3. audiencia (payment),
    //   4. expiración,
    //   5. que el token haya sido emitido para Booking (policy BookingService).
    //
    // El FallbackPolicy hace que cualquier MapGrpcService que se agregue en el futuro
    // quede protegido por defecto: es más seguro que acordarse de poner [Authorize].
    // /health se marca AllowAnonymous explícitamente.
    public static IServiceCollection AddPaymentAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(KeycloakOptions.SectionName)
            .Get<KeycloakOptions>() ?? new KeycloakOptions();

        Validate(options);
        services.Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.SectionName));

        var metadataAddress = string.IsNullOrWhiteSpace(options.MetadataAddress)
            ? $"{options.Authority.TrimEnd('/')}/.well-known/openid-configuration"
            : options.MetadataAddress;

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MetadataAddress = metadataAddress;
                jwt.Audience = options.Audience;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;

                // Sin el mapeo automático de claims para poder leer "azp" y "sub" tal
                // como los emite Keycloak.
                jwt.MapInboundClaims = false;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Authority,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                    NameClaimType = "azp",
                    RoleClaimType = "roles",
                };

                // Auditoría de seguridad: se registra el intento fallido sin volcar el
                // token (nunca se loguean credenciales). El detalle real queda en el log
                // de Microsoft.IdentityModel con nivel Debug si hiciera falta.
                jwt.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Payment.Security")
                            .LogWarning(
                                "JwtAuthenticationFailed | Path={Path} Error={Error}",
                                context.HttpContext.Request.Path,
                                context.Exception.GetType().Name);
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Payment.Security")
                            .LogWarning(
                                "JwtChallenge | Path={Path} Error={Error}",
                                context.HttpContext.Request.Path,
                                context.Error ?? "missing_or_invalid_token");
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddSingleton<IAuthorizationHandler, AllowedClientHandler>();
        services.AddAuthorization(authorization =>
        {
            authorization.AddPolicy(
                AuthorizationPolicies.BookingService,
                policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new AllowedClientRequirement(options.AllowedClients)));

            authorization.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new AllowedClientRequirement(options.AllowedClients))
                .Build();
        });

        return services;
    }

    private static void Validate(KeycloakOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            throw new InvalidOperationException(
                "Falta Keycloak:Authority (issuer del realm). Revisar appsettings o variables de entorno.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException(
                "Falta Keycloak:Audience. Payment tiene que validar aud=payment.");
        }

        if (options.AllowedClients.Length == 0)
        {
            throw new InvalidOperationException(
                "Keycloak:AllowedClients no puede estar vacío: Payment debe autorizar al menos a Booking.");
        }
    }
}
