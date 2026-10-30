# Plan de Desarrollo: Availability.Api
**Proyecto:** Sistema distribuido de reserva de asientos para eventos (TP Programación 4 - TUP)  
**Microservicio:** `Availability.Api` (Capa de Presentación / REST API)  
**Arquitectura:** Clean Architecture + Dominio Anémico  
**Framework:** .NET 8 / ASP.NET Core Web API  
**Fecha de elaboración:** 29 de octubre de 2026

---

## Resumen Ejecutivo

El microservicio `Availability.Api` actúa como la **puerta de entrada pública** del subsistema de disponibilidad. Su responsabilidad principal es:
- Recibir consultas de disponibilidad de asientos
- Validar solicitudes de reserva (1 asiento por reserva)
- Responder inmediatamente con `HTTP 202 Accepted` (sin esperar procesamiento)
- Delegar el procesamiento asincrónico a la capa de aplicación/broker
- Proporcionar trazabilidad e auditoría mediante Correlation ID

**Alcance del componente:** Solo Presentación (REST API) - No incluye EF Core, Azure Service Bus, ni patrones complejos (CQRS, Outbox).

---

## Criterios de Aceptación (AC)

| AC | Descripción | Endpoint | Autenticación |
|---|---|---|---|
| AC1 | Consultar asientos con estado | `GET /api/events/{eventId}/seats` | No requerida |
| AC2 | Solicitar reserva (1 asiento) | `POST /api/reservations` | JWT Keycloak |
| AC3 | Respuesta asíncrona 202 Accepted | `POST /api/reservations` | JWT Keycloak |
| AC4 | Seguimiento de estado de solicitud | `GET /api/reservations/{requestId}` | JWT Keycloak |
| AC5 | Validación JWT (Keycloak) | Global | JWT Keycloak |
| AC6 | Correlation ID + Auditoría | Global Middleware | No requerida |

---

## Stack Tecnológico Confirmado

- **Framework:** .NET 8 / ASP.NET Core Web API
- **Autenticación:** `Microsoft.AspNetCore.Authentication.JwtBearer` (Keycloak)
- **Logging:** `ILogger` nativo + Serilog (opcional)
- **Dependencias:** Solo `Availability.Application` y `Availability.Domain`

### Restricciones Aplicadas

- ✗ NO listas de asientos (`List<int> SeatIds`)
- ✗ NO EF Core, Azure Service Bus, gRPC en la API
- ✗ NO patrones complejos (CQRS, Outbox, MediatR)
- ✗ NO llamadas síncronas bloqueantes a otros microservicios

### Manejo de IDs (GUID)

- **RequestId:** Se genera como `Guid.NewGuid()` en el controlador al crear la solicitud de reserva
- **CorrelationId:** Se genera como `Guid.NewGuid().ToString("N")` si no viene en el header `X-Correlation-ID`
- **EventId y SeatId:** Se reciben como `int` en los DTOs (del request) para compatibilidad con la API de Eventos y Asientos
- **Formato de respuesta:** Los GUIDs se devuelven como strings en formato UUID estándar (ej: `"a4f1b8e0-8902-4b21-9a2f-1c3d5e7f9b0a"`)

---

## Desglose del Plan: 4 Fases

### **FASE 1: Setup, DTOs y Middlewares Base**
**Objetivo:** Preparar la estructura base del proyecto con DTOs, modelos y middleware de auditoría.

**Estimación:** 2 horas

#### Paso 1.1: Crear estructura de carpetas y DTOs
✅ **COMPLETADO**

- **Carpeta:** `Availability.Api/DTOs/Requests/` y `Availability.Api/DTOs/Responses/`
- **DTOs creados:**
  - `CreateReservationRequest.cs` (eventId, seatId)
  - `ReservationResponse.cs` (requestId, status, message)
  - `GetSeatsResponse.cs` (seatId, status, eventId)
  - `GetReservationStatusResponse.cs` (requestId, status, createdAt, updatedAt)

**Resumen de cambios:** Creadas 4 clases DTO con propiedades documentadas, estructura de carpetas organizadas por tipo de transferencia (Requests/Responses). Compilación exitosa.

**Tiempo real:** 0.5 horas

---

#### Paso 1.2: Implementar Correlation ID Middleware
✅ **COMPLETADO**

- **Ubicación:** `Availability.Api/Middleware/CorrelationIdMiddleware.cs`
- **Funcionalidad Implementada:**
  - ✅ Leer header `X-Correlation-ID` si existe
  - ✅ Generar nuevo `Guid.ToString("N")` si no viene (formato sin guiones)
  - ✅ Inyectar en `HttpContext.Items["CorrelationId"]` para acceso global
  - ✅ Agregar al response header para que el cliente reciba su número de seguimiento
  - ✅ Registrar logs de auditoría al recibir y emitir respuesta

- **Registro en Program.cs:** `app.UseMiddleware<CorrelationIdMiddleware>();` ✅ (insertado ANTES de MapControllers)

**Cambios Realizados:**

1. **Archivo Creado:** `src/Availability/Availability.Api/Middleware/CorrelationIdMiddleware.cs`
   - Implementación completa del middleware
   - Manejo seguro de nullable references (`.ToString()` en header reading)
   - Logs estructurados de auditoría (recepción y respuesta)

2. **Archivo Actualizado:** `src/Availability/Availability.Api/Program.cs`
   - Agregado: `using Availability.Api.Middleware;`
   - Agregado: `app.UseMiddleware<CorrelationIdMiddleware>();` antes de `MapControllers()`

**Validación:**
- ✅ Compilación exitosa (primera: 1 warning de nullable, corregido)
- ✅ Segunda compilación exitosa SIN advertencias
- ✅ Código sigue convenciones del proyecto
- ✅ Listo para pruebas de integración

**Tiempo real:** 0.5 horas (incluye corrección de warnings)

---

#### Paso 1.3: Configurar logging base en Program.cs
- Registrar `ILogger<T>` en contenedor DI
- Configurar Serilog (si se aplica) o Console Logger
- Establecer niveles de log (Development vs Production)

**Tiempo estimado:** 0.5 horas

---

**Subtotal Fase 1: 2 horas**

---

### **FASE 2: Configuración de Autenticación y Autorización JWT**
**Objetivo:** Integrar JWT Bearer con Keycloak y proteger endpoints.

**Estimación:** 2.5 horas

#### Paso 2.1: Agregar paquetes NuGet necesarios
- `Microsoft.AspNetCore.Authentication.JwtBearer`
- `System.IdentityModel.Tokens.Jwt`

**Comando:**
```bash
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.x
dotnet add package System.IdentityModel.Tokens.Jwt --version 7.1.x
```

**Tiempo estimado:** 0.25 horas

---

#### Paso 2.2: Crear configuración de JWT en appsettings.json
- Añadir sección `JwtSettings` o `Authentication`
- Incluir: Authority (Keycloak URL), Audience, RequireHttpsMetadata

**Ejemplo:**
```json
{
  "JwtSettings": {
	"Authority": "https://keycloak-server/auth/realms/your-realm",
	"Audience": "availability-api",
	"RequireHttpsMetadata": true,
	"ValidateIssuerSigningKey": true,
	"ValidateLifetime": true
  }
}
```

**Tiempo estimado:** 0.25 horas

---

#### Paso 2.3: Configurar JWT Bearer en Program.cs
- Registrar `AddAuthentication()` y `AddJwtBearer()`
- Configurar `AddAuthorization()`
- Aplicar middleware `app.UseAuthentication()` y `app.UseAuthorization()`

**Código de ejemplo:**

```csharp
// En Program.cs
var jwtSettings = builder.Configuration.GetSection("JwtSettings");

builder.Services
	.AddAuthentication(options =>
	{
		options.DefaultAuthenticateScheme = "Bearer";
		options.DefaultChallengeScheme = "Bearer";
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

builder.Services.AddAuthorization();

// ... resto de servicios ...

var app = builder.Build();

// Middleware
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

**Tiempo estimado:** 1 hora

---

#### Paso 2.4: Crear política de autorización (opcional, por si se requiere)
- Definir atributo `[Authorize]` en controladores protegidos
- Validar presencia de claims específicos si es necesario

**Tiempo estimado:** 0.25 horas

---

#### Paso 2.5: Implementar extracción de UserId desde JWT Claims
- Crear helper o extensión para obtener `sub` (subject/UserId) del token
- Validar que el claim existe y no está vacío

**Código de ejemplo:**

```csharp
// En un controlador o servicio
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

	[HttpPost]
	[Authorize]
	public async Task<IActionResult> CreateReservation([FromBody] CreateReservationRequest request)
	{
		// Extraer UserId del JWT
		var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) 
			?? User.FindFirst("sub");

		if (userIdClaim == null)
		{
			_logger.LogWarning("UserId no encontrado en JWT claims");
			return Unauthorized("UserId no válido");
		}

		var userId = userIdClaim.Value;
		var correlationId = HttpContext.Items["CorrelationId"]?.ToString();

		// Log de auditoría
		_logger.LogInformation(
			"Solicitud de reserva | CorrelationId: {CorrelationId} | UserId: {UserId} | EventId: {EventId} | SeatId: {SeatId}",
			correlationId,
			userId,
			request.EventId,
			request.SeatId
		);

		// Invocar caso de uso
		var result = await _useCase.ExecuteAsync(userId, request.EventId, request.SeatId);

		_logger.LogInformation(
			"Respuesta 202 Accepted | CorrelationId: {CorrelationId} | RequestId: {RequestId}",
			correlationId,
			result.RequestId
		);

		return Accepted(new ReservationResponse
		{
			RequestId = result.RequestId,
			Status = "RECEIVED",
			Message = "Solicitud recibida correctamente"
		});
	}
}
```

**Tiempo estimado:** 0.75 horas

---

**Subtotal Fase 2: 2.5 horas**

---

### **FASE 3: Implementación de Controladores y Endpoints**
**Objetivo:** Crear controladores delgados que reciben DTOs, validan y delegan en casos de uso.

**Estimación:** 2.5 horas

#### Paso 3.1: Crear controlador `SeatAvailabilityController`
- **Endpoint:** `GET /api/events/{eventId}/seats`
- **Autenticación:** No requerida (público)
- **Acción:** Delegar en `IGetSeatsAvailabilityUseCase`
- **Respuesta:** Lista de asientos con estado

**Carpeta:** `Availability.Api/Controllers/SeatAvailabilityController.cs`

**Tiempo estimado:** 0.75 horas

---

#### Paso 3.2: Crear controlador `ReservationsController`
- **Endpoints:**
  - `POST /api/reservations` - Crear solicitud (202 Accepted)
  - `GET /api/reservations/{requestId}` - Consultar estado

- **Métodos:**
  - `CreateReservation(CreateReservationRequest)` - Protegido con `[Authorize]`
  - `GetReservationStatus(string requestId)` - Protegido con `[Authorize]`

- **Validaciones en controlador:**
  - DTO no nulo
  - eventId > 0
  - seatId > 0
  - requestId no vacío

**Carpeta:** `Availability.Api/Controllers/ReservationsController.cs`

**Tiempo estimado:** 1 hora

---

#### Paso 3.3: Configurar rutas y atributos globales
- Aplicar `[ApiController]` en controladores
- Definir `[Route("api/[controller]")]`
- Configurar `[Produces("application/json")]`
- Aplicar `[ApiVersion]` si se requiere versionado

**Tiempo estimado:** 0.25 horas

---

#### Paso 3.4: Implementar validación de modelo (ModelState)
- Validar que `ModelState.IsValid`
- Retornar `BadRequest(ModelState)` si no es válido
- Loguear errores de validación con CorrelationId

**Tiempo estimado:** 0.25 horas

---

#### Paso 3.5: Crear respuestas de error estándar
- Clase base: `ErrorResponse` (code, message, correlationId)
- Aplicar en excepciones no controladas (si hay middleware de manejo de errores)

**Tiempo estimado:** 0.25 horas

---

**Subtotal Fase 3: 2.5 horas**

---

### **FASE 4: Inyección de Dependencias y Manejo de Errores**
**Objetivo:** Conectar la API con los casos de uso, configurar DI y establecer estrategia de manejo de errores.

**Estimación:** 2 horas

#### Paso 4.1: Definir contratos de casos de uso en Application
- **Interfaces a crear en `Availability.Application/UseCases/`:**
  - `IGetSeatsAvailabilityUseCase` - Retorna lista de asientos
  - `ICreateReservationRequestUseCase` - Crea solicitud y retorna requestId
  - `IGetReservationStatusUseCase` - Retorna estado de solicitud

- **Retornos esperados:**
  - `GetSeatsAvailabilityUseCase` → `GetSeatsAvailabilityOutput` (List<SeatDto>)
  - `CreateReservationRequestUseCase` → `CreateReservationRequestOutput` (RequestId, Status)
  - `GetReservationStatusUseCase` → `GetReservationStatusOutput` (RequestId, Status, CreatedAt, UpdatedAt)

**Nota:** Los casos de uso serán mockeados o implementados en capas de Infraestructura (fuera del alcance actual).

**Tiempo estimado:** 0.75 horas

---

#### Paso 4.2: Registrar dependencias en Program.cs
- Agregar extensión `builder.Services.AddApplicationServices()`
- Registrar casos de uso e interfaces
- Registrar servicios auxiliares (logging, etc.)

**Código de ejemplo:**

```csharp
// En Program.cs
builder.Services
	.AddScoped<IGetSeatsAvailabilityUseCase, GetSeatsAvailabilityUseCase>()
	.AddScoped<ICreateReservationRequestUseCase, CreateReservationRequestUseCase>()
	.AddScoped<IGetReservationStatusUseCase, GetReservationStatusUseCase>();

// O mediante extensión en Application:
builder.Services.AddApplicationServices();
```

**Tiempo estimado:** 0.5 horas

---

#### Paso 4.3: Implementar middleware de manejo global de excepciones
- **Ubicación:** `Availability.Api/Middleware/ExceptionHandlingMiddleware.cs`
- **Comportamiento:**
  - Capturar excepciones no controladas
  - Loguear con CorrelationId
  - Retornar `ErrorResponse` con StatusCode 500

**Tiempo estimado:** 0.5 horas

**Código de ejemplo:**

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Availability.Api.Middleware
{
	public class ExceptionHandlingMiddleware
	{
		private readonly RequestDelegate _next;
		private readonly ILogger<ExceptionHandlingMiddleware> _logger;

		public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
		{
			_next = next;
			_logger = logger;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			try
			{
				await _next(context);
			}
			catch (Exception ex)
			{
				var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";
				_logger.LogError(ex,
					"Excepción no controlada | CorrelationId: {CorrelationId} | Message: {Message}",
					correlationId,
					ex.Message);

				context.Response.StatusCode = StatusCodes.Status500InternalServerError;
				context.Response.ContentType = "application/json";

				var response = new
				{
					code = "INTERNAL_SERVER_ERROR",
					message = "Error interno del servidor",
					correlationId = correlationId
				};

				await context.Response.WriteAsJsonAsync(response);
			}
		}
	}
}
```

**Tiempo estimado:** 0.5 horas

---

#### Paso 4.4: Validar configuración y pruebas funcionales básicas
- Verificar que los endpoints responden correctamente
- Validar que la autenticación JWT funciona
- Confirmar que Correlation ID se propaga
- Revisar logs de auditoría

**Tiempo estimado:** 0.25 horas

---

**Subtotal Fase 4: 2 horas**

---

## Resumen de Estimaciones

| Fase | Descripción | Horas | Acumulado |
|---|---|---:|---:|
| **Fase 1** | Setup, DTOs y Middlewares Base | 2.0 | 2.0 |
| **Fase 2** | Configuración JWT + Keycloak | 2.5 | 4.5 |
| **Fase 3** | Implementación de Controladores | 2.5 | 7.0 |
| **Fase 4** | Inyección de Dependencias + Manejo de Errores | 2.0 | 9.0 |
| **TOTAL** | | **9.0 horas** | **9.0 horas** |

---

## Checklist de Completación

### Fase 1 ✅ (Parcialmente Completada)
- [x] Crear estructura de carpetas (DTOs, Middleware)
- [x] Implementar `CorrelationIdMiddleware.cs` ✅ **PASO 1.2 COMPLETADO**
- [x] Crear clases DTO (Request, Response)
- [ ] Configurar logging base en `Program.cs` (Paso 1.3 - PENDIENTE)

### Fase 2 ✅
- [ ] Agregar paquetes NuGet JWT Bearer
- [ ] Configurar `appsettings.json` con JWT Settings
- [ ] Implementar `AddJwtBearer()` en `Program.cs`
- [ ] Registrar middleware `UseAuthentication()` y `UseAuthorization()`
- [ ] Implementar extracción de UserId desde claims JWT en controlador

### Fase 3 ✅
- [ ] Crear `SeatAvailabilityController` con endpoint `GET /api/events/{eventId}/seats`
- [ ] Crear `ReservationsController` con endpoints `POST /api/reservations` y `GET /api/reservations/{requestId}`
- [ ] Aplicar atributos `[ApiController]`, `[Route]`, `[Authorize]`
- [ ] Validar `ModelState` en cada acción
- [ ] Crear clase `ErrorResponse`

### Fase 4 ✅
- [ ] Definir interfaces de casos de uso en `Availability.Application`
- [ ] Registrar dependencias en `Program.cs`
- [ ] Implementar `ExceptionHandlingMiddleware.cs`
- [ ] Registrar middleware en `Program.cs`
- [ ] Validar que endpoints responden correctamente
- [ ] Confirmar JWT funciona (401 sin token, 200 con token válido)
- [ ] Revisar logs de auditoría y Correlation ID

---

## Estructura Final de Archivos

```
Availability.Api/
├── Controllers/
│   ├── SeatAvailabilityController.cs
│   └── ReservationsController.cs
├── DTOs/
│   ├── Requests/
│   │   └── CreateReservationRequest.cs
│   └── Responses/
│       ├── ReservationResponse.cs
│       ├── GetSeatsResponse.cs
│       ├── GetReservationStatusResponse.cs
│       └── ErrorResponse.cs
├── Middleware/
│   ├── CorrelationIdMiddleware.cs
│   └── ExceptionHandlingMiddleware.cs
├── Program.cs (actualizado)
├── appsettings.json (actualizado)
├── Availability.Api.csproj (sin cambios)
└── Properties/
	└── launchSettings.json

Availability.Application/
├── UseCases/
│   ├── IGetSeatsAvailabilityUseCase.cs
│   ├── ICreateReservationRequestUseCase.cs
│   └── IGetReservationStatusUseCase.cs
├── DTOs/
│   ├── GetSeatsAvailabilityOutput.cs
│   ├── CreateReservationRequestOutput.cs
│   └── GetReservationStatusOutput.cs
└── DependencyInjection.cs (extensión de ServiceCollection)

Availability.Domain/
└── Entities/ (sin cambios inicialmente)
```

---

## Contatos de Integración (Fuera de Alcance: Fase Posterior)

- **Availability.Infrastructure:** Implementar casos de uso reales, acceso a BD
- **Broker/MQ:** Publicar eventos de reserva a Azure Service Bus/RabbitMQ
- **Booking Microservice:** Consumir eventos y procesar reservas

---

## Próximos Pasos (Guía para Orquestación)

1. **Ejecutar Fase 1:** Preparar estructura y DTOs
2. **Validar Fase 1:** Confirmar que el proyecto compila sin errores
3. **Ejecutar Fase 2:** Integrar JWT + Keycloak
4. **Validar Fase 2:** Probar autenticación con Postman/Insomnia
5. **Ejecutar Fase 3:** Implementar controladores
6. **Validar Fase 3:** Probar endpoints (sin lógica de negocio real)
7. **Ejecutar Fase 4:** Conectar DI + manejo de errores
8. **Validar Fase 4:** Pruebas funcionales integrales + logs

---

## Notas Importantes

- **Sin cambios de código:** Este plan es únicamente **guía y especificación**. Se espera orquestación manual del desarrollo.
- **Validación de requisitos:** Cada fase debe validar contra los AC definidos.
- **Código limpio:** Aplicar principios SOLID, naming claro y comentarios solo donde sea necesario.
- **Logging consistente:** Siempre incluir `CorrelationId` en logs de auditoría.
- **Pendiente de escalación:** Si `Availability.Application` o `Availability.Domain` necesitan expansión, se coordina en próximo sprint.


## Análisis Adicional: Estrategia de Commits

### Recomendación: Commits por **Sub-fase** (granularidad media)
Usar formato `feat(availability-api/phase-N): [descripción]` para claridad y trazabilidad.

---

**Plan elaborado por:** Tech Lead Senior .NET  
**Fecha:** 29 de octubre de 2026  
**Estado:** Pendiente de ejecución y orquestación
