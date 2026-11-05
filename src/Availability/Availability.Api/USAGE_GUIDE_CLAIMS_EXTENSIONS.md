# Guía de Uso: Extensiones de ClaimsPrincipal

**Objetivo:** Demostrar cómo utilizar la clase `ClaimsPrincipalExtensions` en los controladores de la API para extraer información del JWT de Keycloak.

**Ubicación:** `Availability.Api/Extensions/ClaimsPrincipalExtensions.cs`

---

## Métodos Disponibles

### 1. `GetUserId()` - Extraer Identificador del Usuario

Extrae el `sub` (subject) del JWT, que es el identificador único del usuario en Keycloak.

**Firma:**
```csharp
public static string? GetUserId(this ClaimsPrincipal principal)
```

**Ejemplo de uso en controlador:**
```csharp
[HttpPost("reservations")]
[Authorize]
public async Task<IActionResult> CreateReservation([FromBody] CreateReservationRequest request)
{
	var userId = User.GetUserId();

	if (string.IsNullOrWhiteSpace(userId))
	{
		_logger.LogWarning("UserId no encontrado en JWT claims");
		return BadRequest("UserId inválido");
	}

	_logger.LogInformation("Usuario {UserId} solicita reserva", userId);

	// Delegar al caso de uso
	var result = await _useCase.ExecuteAsync(userId, request.EventId, request.SeatId);

	return Accepted(result);
}
```

---

### 2. `GetEmail()` - Extraer Email del Usuario

Obtiene el email del usuario del JWT.

**Firma:**
```csharp
public static string? GetEmail(this ClaimsPrincipal principal)
```

**Ejemplo:**
```csharp
var email = User.GetEmail();
_logger.LogInformation("Solicitud desde {Email}", email ?? "usuario anónimo");
```

---

### 3. `GetName()` - Extraer Nombre del Usuario

Obtiene el nombre del usuario del JWT.

**Firma:**
```csharp
public static string? GetName(this ClaimsPrincipal principal)
```

---

### 4. `GetRoles()` - Extraer Roles del Usuario

Obtiene la lista de roles del usuario del JWT.

**Firma:**
```csharp
public static IEnumerable<string> GetRoles(this ClaimsPrincipal principal)
```

**Ejemplo:**
```csharp
var roles = User.GetRoles();
if (roles.Contains("admin"))
{
	// Operaciones solo para administradores
}
```

---

### 5. `HasValidUserId()` - Validar que el UserId es Válido

Valida que el usuario tiene un UserId no vacío en el JWT.

**Firma:**
```csharp
public static bool HasValidUserId(this ClaimsPrincipal principal)
```

**Ejemplo:**
```csharp
[Authorize]
public IActionResult GetStatus()
{
	if (!User.HasValidUserId())
		return Unauthorized("UserId inválido");

	// Continuar con la lógica
}
```

---

### 6. `HasRole()` - Validar Rol Específico

Valida que el usuario tiene un rol específico.

**Firma:**
```csharp
public static bool HasRole(this ClaimsPrincipal principal, string role)
```

**Ejemplo:**
```csharp
if (User.HasRole("admin"))
{
	// Ejecutar lógica administrativa
}
```

---

### 7. `GetClaimValue()` - Extraer Claim Personalizado

Obtiene un claim específico por nombre (útil para claims personalizados de Keycloak).

**Firma:**
```csharp
public static string? GetClaimValue(this ClaimsPrincipal principal, string claimType)
```

**Ejemplo:**
```csharp
var customClaim = User.GetClaimValue("department");
var preferredLanguage = User.GetClaimValue("locale");
```

---

## Patrón de Uso Completo en ReservationsController

```csharp
using Availability.Api.Dtos.Requests;
using Availability.Api.Dtos.Responses;
using Availability.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Availability.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	[Produces("application/json")]
	public class ReservationsController : ControllerBase
	{
		private readonly ILogger<ReservationsController> _logger;
		private readonly ICreateReservationRequestUseCase _useCase;

		public ReservationsController(
			ILogger<ReservationsController> logger,
			ICreateReservationRequestUseCase useCase)
		{
			_logger = logger;
			_useCase = useCase;
		}

		/// <summary>
		/// Crear una solicitud de reserva.
		/// Requiere autenticación JWT válida.
		/// Responde inmediatamente con 202 Accepted.
		/// </summary>
		[HttpPost]
		[Authorize]
		public async Task<IActionResult> CreateReservation([FromBody] CreateReservationRequest request)
		{
			// Validar modelo
			if (!ModelState.IsValid)
				return BadRequest(ModelState);

			// Extraer UserId del JWT usando la extensión
			var userId = User.GetUserId();
			if (string.IsNullOrWhiteSpace(userId))
			{
				_logger.LogWarning("UserId no encontrado en JWT claims");
				return Unauthorized(new { error = "UserId no válido" });
			}

			// Extraer CorrelationId del contexto (agregado en CorrelationIdMiddleware)
			var correlationId = HttpContext.Items["CorrelationId"]?.ToString();

			// Log de auditoría
			_logger.LogInformation(
				"Solicitud de reserva | CorrelationId: {CorrelationId} | UserId: {UserId} | EventId: {EventId} | SeatId: {SeatId}",
				correlationId,
				userId,
				request.EventId,
				request.SeatId);

			try
			{
				// Delegar al caso de uso
				var result = await _useCase.ExecuteAsync(userId, request.EventId, request.SeatId);

				_logger.LogInformation(
					"Respuesta 202 Accepted | CorrelationId: {CorrelationId} | RequestId: {RequestId}",
					correlationId,
					result.RequestId);

				return Accepted(new ReservationResponse
				{
					RequestId = result.RequestId,
					Status = "PENDING",
					Message = "Solicitud recibida correctamente",
					CreatedAt = DateTime.UtcNow
				});
			}
			catch (DomainException ex)
			{
				_logger.LogWarning(
					"Error en solicitud de reserva | CorrelationId: {CorrelationId} | Error: {Error}",
					correlationId,
					ex.Message);
				return BadRequest(new { error = ex.Message });
			}
		}

		/// <summary>
		/// Obtener el estado de una solicitud de reserva.
		/// Requiere autenticación JWT válida.
		/// </summary>
		[HttpGet("{requestId}")]
		[Authorize]
		public async Task<IActionResult> GetReservationStatus(string requestId)
		{
			// Validar que el usuario está autenticado
			if (!User.HasValidUserId())
				return Unauthorized(new { error = "Usuario no autenticado" });

			var userId = User.GetUserId();
			var correlationId = HttpContext.Items["CorrelationId"]?.ToString();

			_logger.LogInformation(
				"Consulta de estado | CorrelationId: {CorrelationId} | UserId: {UserId} | RequestId: {RequestId}",
				correlationId,
				userId,
				requestId);

			try
			{
				var status = await _useCase.ExecuteAsync(userId, requestId);
				return Ok(new GetReservationStatusResponse
				{
					RequestId = requestId,
					Status = status.Status,
					Message = status.Message,
					UpdatedAt = status.UpdatedAt
				});
			}
			catch (NotFoundException ex)
			{
				return NotFound(new { error = ex.Message });
			}
		}
	}
}
```

---

## Consideraciones Importantes

### 1. **Null Safety**
Siempre validar que los valores extraídos no son null, especialmente `GetUserId()`:

```csharp
var userId = User.GetUserId();
if (string.IsNullOrWhiteSpace(userId))
	return BadRequest("UserId requerido");
```

### 2. **Logs de Auditoría**
Incluir siempre el UserId y CorrelationId en logs de auditoría:

```csharp
_logger.LogInformation(
	"Acción | CorrelationId: {CorrelationId} | UserId: {UserId} | Detalles: {Details}",
	correlationId,
	userId,
	details);
```

### 3. **@Authorize Obligatorio**
Los métodos que usan `User.GetUserId()` deben estar marcados con `[Authorize]`:

```csharp
[HttpPost]
[Authorize]  // ← Requerido
public async Task<IActionResult> CreateReservation(...)
```

### 4. **Integración con CorrelationIdMiddleware**
La extensión es independiente del CorrelationIdMiddleware, pero se usarán juntas:

- `CorrelationIdMiddleware`: Genera/extrae CorrelationId del header `X-Correlation-ID`
- `ClaimsPrincipalExtensions`: Extrae información del JWT

---

## Fase 2.5 - Completado ✅

Esta guía documenta la extensión `ClaimsPrincipalExtensions` lista para ser utilizada en los controladores de Fase 3.

**Próximos pasos:**
- Fase 3: Implementar `SeatAvailabilityController` y `ReservationsController`
- Los controladores importarán y usarán esta extensión según el patrón arriba demostrado

