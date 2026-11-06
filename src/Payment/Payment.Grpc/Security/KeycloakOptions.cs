namespace Payment.Grpc.Security;

// Configuración de la validación de tokens de Keycloak (consigna 3e).
//
// DECISIÓN: el issuer público y la metadata se separan en dos valores. En local ambos
// apuntan a http://localhost:8081, pero dentro de Docker el issuer sigue siendo
// http://localhost:8081/realms/seat-reservation (el token lo dice así) mientras la
// metadata hay que pedirla a http://keycloak:8081/... para que el contenedor la resuelva.
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    // Issuer esperado en el claim "iss" (URL pública del realm).
    public string Authority { get; set; } = string.Empty;

    // URL del documento .well-known. Si está vacía se deriva de Authority.
    public string? MetadataAddress { get; set; }

    // Audiencia que debe traer el token para aceptarlo (clientId de Payment).
    public string Audience { get; set; } = "payment";

    // Sólo para desarrollo local: Keycloak corre en HTTP.
    public bool RequireHttpsMetadata { get; set; }

    // Clients autorizados a invocar Payment. En este TP, Booking.
    // Sin default a propósito: el binder de configuración concatena sobre el array
    // existente y duplicaría los valores. Se exige en appsettings/entorno.
    public string[] AllowedClients { get; set; } = [];

    // Tolerancia de reloj al validar exp/iat. Keycloak y los contenedores pueden desfasarse.
    public int ClockSkewSeconds { get; set; } = 30;
}
