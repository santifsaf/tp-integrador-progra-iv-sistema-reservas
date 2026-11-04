using System.Security.Claims;

namespace Availability.Api.Extensions;

/// <summary>
/// Extensiones de <see cref="ClaimsPrincipal"/> para extraer información del JWT de Keycloak.
/// Facilita el acceso a claims comunes como UserId, roles y otros atributos del usuario autenticado.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extrae el UserId del token JWT.
    /// Intenta obtener el claim "sub" (subject - identidad única del usuario en Keycloak).
    /// </summary>
    /// <param name="principal">El ClaimsPrincipal que contiene los claims del JWT</param>
    /// <returns>El UserId como string (UUID de Keycloak), o null si no se encuentra</returns>
    public static string? GetUserId(this ClaimsPrincipal principal)
    {
        if (principal == null)
            return null;

        // Keycloak utiliza "sub" como claim para el identificador único del usuario
        var subClaim = principal.FindFirst("sub") 
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier);

        return subClaim?.Value;
    }

    /// <summary>
    /// Obtiene el email del usuario desde el JWT.
    /// Busca en los claims estándar de OpenID Connect.
    /// </summary>
    /// <param name="principal">El ClaimsPrincipal que contiene los claims del JWT</param>
    /// <returns>El email del usuario, o null si no se encuentra</returns>
    public static string? GetEmail(this ClaimsPrincipal principal)
    {
        if (principal == null)
            return null;

        return principal.FindFirst("email")?.Value 
               ?? principal.FindFirst(ClaimTypes.Email)?.Value;
    }

    /// <summary>
    /// Obtiene el nombre del usuario desde el JWT.
    /// Busca el claim "name" de OpenID Connect.
    /// </summary>
    /// <param name="principal">El ClaimsPrincipal que contiene los claims del JWT</param>
    /// <returns>El nombre del usuario, o null si no se encuentra</returns>
    public static string? GetName(this ClaimsPrincipal principal)
    {
        if (principal == null)
            return null;

        return principal.FindFirst("name")?.Value 
               ?? principal.FindFirst(ClaimTypes.Name)?.Value;
    }

    /// <summary>
    /// Obtiene los roles del usuario desde el JWT.
    /// En Keycloak, típicamente vienen en el claim "realm_access" o "resource_access".
    /// Este método busca roles en los claims estándar.
    /// </summary>
    /// <param name="principal">El ClaimsPrincipal que contiene los claims del JWT</param>
    /// <returns>Lista de roles del usuario</returns>
    public static IEnumerable<string> GetRoles(this ClaimsPrincipal principal)
    {
        if (principal == null)
            return Enumerable.Empty<string>();

        return principal.FindAll(ClaimTypes.Role)
                       .Select(c => c.Value);
    }

    /// <summary>
    /// Valida que el usuario tiene un UserId válido en el JWT.
    /// Útil para endpoints protegidos que requieren identificación del usuario.
    /// </summary>
    /// <param name="principal">El ClaimsPrincipal que contiene los claims del JWT</param>
    /// <returns>true si el UserId existe y no está vacío; false en caso contrario</returns>
    public static bool HasValidUserId(this ClaimsPrincipal principal)
    {
        var userId = principal.GetUserId();
        return !string.IsNullOrWhiteSpace(userId);
    }

    /// <summary>
    /// Valida que el usuario tiene un rol específico.
    /// Útil para autorización basada en roles.
    /// </summary>
    /// <param name="principal">El ClaimsPrincipal que contiene los claims del JWT</param>
    /// <param name="role">El rol a verificar</param>
    /// <returns>true si el usuario tiene el rol; false en caso contrario</returns>
    public static bool HasRole(this ClaimsPrincipal principal, string role)
    {
        if (principal == null || string.IsNullOrWhiteSpace(role))
            return false;

        return principal.IsInRole(role);
    }

    /// <summary>
    /// Obtiene un claim específico por nombre.
    /// Útil para acceder a claims personalizados de Keycloak.
    /// </summary>
    /// <param name="principal">El ClaimsPrincipal que contiene los claims del JWT</param>
    /// <param name="claimType">El tipo/nombre del claim a buscar</param>
    /// <returns>El valor del claim, o null si no se encuentra</returns>
    public static string? GetClaimValue(this ClaimsPrincipal principal, string claimType)
    {
        if (principal == null || string.IsNullOrWhiteSpace(claimType))
            return null;

        return principal.FindFirst(claimType)?.Value;
    }
}
