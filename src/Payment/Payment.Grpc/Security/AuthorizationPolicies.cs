using Microsoft.AspNetCore.Authorization;

namespace Payment.Grpc.Security;

public static class AuthorizationPolicies
{
    /// <summary>Sólo el client credentials de Booking puede ejecutar cobros.</summary>
    public const string BookingService = "BookingService";
}

// Autorización por client de Keycloak, además de la validación estándar del JWT
// (firma, issuer, audiencia, expiración).
//
// DECISIÓN: autenticar no es autorizar. Cualquier client del realm con aud=payment podría
// presentar un token válido; el requisito extra exige que el token haya sido emitido para
// Booking. Keycloak pone el client en el claim "azp" (authorized party). "client_id" se
// acepta como fallback por si se cambia el mapper del realm.
public sealed class AllowedClientRequirement(params string[] clientIds) : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> ClientIds { get; } = clientIds;
}

public sealed class AllowedClientHandler : AuthorizationHandler<AllowedClientRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AllowedClientRequirement requirement)
    {
        var clientId = context.User.FindFirst("azp")?.Value
            ?? context.User.FindFirst("client_id")?.Value;

        if (!string.IsNullOrWhiteSpace(clientId) &&
            requirement.ClientIds.Contains(clientId, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
