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

## 🔴 OBSERVACIÓN CRÍTICA: Mapeo int ↔ Guid en Arquitectura Distribuida

> **Estado:** Identificado en code review de Fase 1  
> **Impacto:** Afecta diseño de controladores y casos de uso (Fase 3)  
> **Resolución:** Será documentado en ARCHITECTURE.md antes de Fase 3

### La Contradicción

El sistema presenta una **dualidad de tipos** intencional:

```
DTOs de API (int)              Dominio Interno (Guid)
┌──────────────────┐           ┌──────────────────┐
│ EventId: int     │ ──────→ │ Event.Id: Guid   │
│ SeatId: int      │ ──────→ │ Seat.Id: Guid    │
└──────────────────┘           └──────────────────┘
```

**Razón:**
- **Entrada (HTTP API):** Los clientes reciben `eventId=15` y `seatId=42` como `int` (del catálogo público de Eventos Service)
- **Interno (Dominio):** El dominio trabaja con `Guid` internamente (fuente de verdad distribuida, única garantía en microservicios)

**Consistencia con documentación del TP:**
- ✅ Cumple con "compatibilidad con la API de Eventos y Asientos"
- ✅ Mantiene GUID como "fonte de verdad" en Availability (ver TP documento consolidado, sección 2.1)

### Dónde Ocurre la Conversión

La conversión de tipos debe ocurrir en la **capa Application (UseCases)**, nunca en el controlador:

**Flujo esperado:**

1. **Cliente HTTP** → `POST /api/reservations { "eventId": 15, "seatId": 42 }`
2. **Controlador recibe** → `CreateReservationRequest` con propiedades `int`
3. **Controlador invoca** → `ICreateReservationUseCase.ExecuteAsync(int eventId, int seatId, Guid userId)`
4. **UseCase internamente:**
   - Valida que EventId (int) existe en repositorio → obtiene `Event` con `Guid`
   - Valida que SeatId (int) existe en ese evento → obtiene `Seat` con `Guid`
   - Crea `ReservationRequest` entity con IDs como `Guid`
5. **Respuesta** → `ReservationResponse` con `requestId` como `Guid` (string en JSON)

**El mapeo NO es explícito una conversión `int.ToGuid()`, sino:**
- Consultar qué `Guid` real corresponde a ese `int` externo
- Usar ese `Guid` en el dominio

### Métodos de Repositorio Necesarios (Fase 3)

Para implementar esta conversión, se necesitarán métodos adicionales:

```csharp
// En Availability.Application/Abstractions/IEventRepository.cs
public interface IEventRepository
{
    Task<Event?> GetByIdAsync(Guid eventId, CancellationToken ct);

    // 🔴 NUEVO: Conversión int → Guid
    /// <summary>
    /// Obtiene un evento por su ID externo (int del servicio de Eventos).
    /// 
    /// Esta es la "puerta de entrada" que convierte:
    /// int (del cliente) → Guid (del dominio)
    /// </summary>
    Task<Event?> GetByExternalIdAsync(int externalEventId, CancellationToken ct);
}

// En Availability.Application/Abstractions/ISeatRepository.cs
public interface ISeatRepository
{
    Task<IReadOnlyList<Seat>> GetByEventAsync(Guid eventId, CancellationToken ct);
    Task<bool> ExistsAsync(Guid eventId, Guid seatId, CancellationToken ct);

    // 🔴 NUEVO: Conversión int, int → Seat con Guid
    /// <summary>
    /// Valida que un asiento existe por IDs externos (int).
    /// Retorna la entidad Seat con Guid internos.
    /// </summary>
    Task<Seat?> GetByExternalIdsAsync(int externalEventId, int externalSeatId, CancellationToken ct);
}
```

### Ejemplo de Implementación en UseCase (Fase 3)

```csharp
public class CreateReservationUseCase : ICreateReservationRequestUseCase
{
    private readonly IEventRepository _eventRepository;
    private readonly ISeatRepository _seatRepository;
    private readonly IReservationRequestRepository _reservationRepository;
    private readonly IReservationPublisher _publisher;

    public async Task<RequestReceivedDto> ExecuteAsync(
        int eventId,          // ← int de la API
        int seatId,           // ← int de la API
        Guid userId,          // ← Guid del JWT
        CancellationToken ct)
    {
        // PASO 1: Convertir int → Guid usando repositorio
        var event = await _eventRepository.GetByExternalIdAsync(eventId, ct);
        if (event == null)
            throw new DomainException("EventoNoEncontrado");

        var seat = await _seatRepository.GetByExternalIdsAsync(eventId, seatId, ct);
        if (seat == null)
            throw new DomainException("AsientoNoEncontrado");

        // PASO 2: Ahora trabajamos con Guid
        var reservationRequest = new ReservationRequest
        {
            Id = Guid.NewGuid(),
            EventId = event.Id,        // ✅ Guid (obtenido del repositorio)
            SeatId = seat.Id,          // ✅ Guid (obtenido del repositorio)
            UserId = userId,           // ✅ Guid (del JWT)
            Status = RequestStatus.Received,
            CreatedAt = DateTime.UtcNow
        };

        // PASO 3: Persistir y publicar
        await _reservationRepository.SaveAsync(reservationRequest, ct);
        await _publisher.PublishAsync(reservationRequest, ct);

        return new RequestReceivedDto(
            reservationRequest.Id,
            nameof(RequestStatus.Received)
        );
    }
}
```

### Riesgos a Mitigar

| Riesgo | Impacto | Mitigación |
|--------|---------|-----------|
| **EventId (int) inexistente** | Excepción en validación | `GetByExternalIdAsync()` retorna null si no existe |
| **SeatId (int) pertenece a otro evento** | Validación incorrecta | `GetByExternalIdsAsync()` valida ambos IDs juntos |
| **Desincronización: Evento eliminado en servicio Eventos** | Referencias rotas | No manejable en Availability (delegado a Booking después) |
| **Confusión en código: ¿int o Guid?** | Bugs de asignación | Documentar con XML comments en DTOs |

### Pendiente de Documentación Formal

- [ ] Crear `docs/ARCHITECTURE.md` con sección "Mapeo de IDs: int ↔ Guid"
- [ ] Agregar XML comments en `CreateReservationRequest.cs`
- [ ] Documentar contrato de `GetByExternalIdAsync()` y `GetByExternalIdsAsync()`

> ⚠️ **NO se realizarán cambios de código hasta Fase 3**, solo se documenta aquí la decisión tomada para que sea explícita desde ahora.

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
✅ **COMPLETADO**

- **Ubicación:** `src/Availability/Availability.Api/Program.cs`
- **Funcionalidad Implementada:**
  - ✅ Registrar `ILogger<T>` mediante Serilog en contenedor DI
  - ✅ Configurar Serilog como proveedor principal (reemplazando default loggers)
  - ✅ Establecer niveles de log: **Information** (Development + Production)
  - ✅ Doble salida: Consola + Archivo con rotación diaria
  - ✅ Template estructurado con timestamp, nivel, mensaje y excepciones
  - ✅ Enrichment de logs: contexto global + propiedad personalizada "Service"
  - ✅ Gestión del ciclo de vida: Try-catch-finally con logs de auditoría

**Cambios Realizados:**

1. **Archivo Actualizado:** `src/Availability/Availability.Api/Program.cs`
   - Agregado: `using Serilog;`
   - Agregado: `builder.Services.AddLogging()` con Serilog integrado
   - Configuración centralizada de Serilog:
     - MinimumLevel: Information
     - WriteTo.Console con template personalizado
     - WriteTo.File con rotación diaria (`logs/availability-api-{date}.txt`)
     - Enrich.FromLogContext() para capturar contexto
     - Enrich.WithProperty("Service", "Availability.Api")
   - Try-catch-finally:
     - Log.Information("Iniciando aplicación Availability.Api")
     - Log.Fatal() para excepciones no manejadas
     - Log.CloseAndFlush() para guarantee de escritura

2. **Archivo Actualizado:** `src/Availability/Availability.Api/Availability.Api.csproj`
   - Agregado PackageReference: `Serilog` v4.0.0
   - Agregado PackageReference: `Serilog.AspNetCore` v8.0.0
   - Agregado PackageReference: `Serilog.Sinks.Console` v6.0.0
   - Agregado PackageReference: `Serilog.Sinks.File` v5.0.0

**Validación:**
- ✅ Compilación exitosa
- ✅ Paquetes NuGet instalados y compatible con .NET 8/10
- ✅ Logs disponibles en consola y archivo `logs/` automáticamente
- ✅ Integración con Correlation ID Middleware lista
- ✅ Pronto para FASE 2 (autenticación JWT)

**Tiempo real:** 0.5 horas

---

**Subtotal Fase 1: 1.5 horas (cronometrado)**

---

### **FASE 2: Configuración de Autenticación y Autorización JWT**
**Objetivo:** Integrar JWT Bearer con Keycloak y proteger endpoints.

**Estimación:** 2.5 horas

#### Paso 2.1: Agregar paquetes NuGet necesarios ✅ COMPLETADO

- `Microsoft.AspNetCore.Authentication.JwtBearer` v8.0.0 ✅
- `System.IdentityModel.Tokens.Jwt` v7.1.2 ✅

**Descripción:**
Se han agregado exitosamente los dos paquetes NuGet necesarios para la autenticación JWT en el archivo `Availability.Api.csproj`. Estos paquetes permiten:
- **JwtBearer:** Validar automáticamente cada JWT recibido en los requests
- **IdentityModel.Tokens:** Leer y extraer información desde los claims del JWT (user ID, roles, etc.)

**Validación:**
- ✅ Compilación exitosa en 48.7 segundos
- ✅ Todas las dependencias resueltas correctamente
- ✅ Paquetes compatibles con .NET 10.0

**Comando ejecutado:**
```bash
dotnet build "src/Availability/Availability.Api/Availability.Api.csproj" --configuration Debug
```

**Tiempo real:** 0.25 horas

**Estado:** ✅ COMPLETADO

---

#### Paso 2.2: Crear configuración de JWT en appsettings.json ✅ COMPLETADO

**Descripción:**
Se agregó la sección `JwtSettings` en `appsettings.json` con configuración para validar tokens JWT desde Keycloak. La configuración incluye:
- **Authority:** `http://keycloak:8081/realms/seat-reservation` (Docker) - se sobrescribe a `http://localhost:8081/realms/seat-reservation` en `appsettings.Development.json`
- **Audience:** `availability-api` (validar que el JWT es para este servicio)
- **RequireHttpsMetadata:** `false` (desarrollo)
- **ValidateIssuerSigningKey:** `true` (verifica firma del JWT)
- **ValidateLifetime:** `true` (rechaza JWTs expirados)

**Archivos Modificados:**
- `src/Availability/Availability.Api/appsettings.json` → Configuración base (todos los parámetros)
- `src/Availability/Availability.Api/appsettings.Development.json` → Override solo de Authority (merge automático)

**Validación:**
- ✅ Compilación exitosa
- ✅ Configuración correcta según infraestructura de Keycloak (repo de Feli)
- ✅ JSON válido y accesible desde `Program.cs` mediante `GetSection("JwtSettings")`

**Tiempo real:** 0.5 horas

---

#### Paso 2.3: Configurar JWT Bearer en Program.cs ✅ COMPLETADO

Using Statements Agregados
	using Microsoft.AspNetCore.Authentication;
	using Microsoft.AspNetCore.Authentication.JwtBearer;
	using Microsoft.IdentityModel.Tokens;

Configuración JWT en Program.cs (Step 2-6)
•	 Lectura de JwtSettings desde appsettings.json
•	 Registro de AddAuthentication() con esquema Bearer predeterminado
•	 Configuración de AddJwtBearer() con:
	•	Authority desde configuración
	•	Audience (availability-api)
	•	RequireHttpsMetadata (parseado como bool con default true)
	•	TokenValidationParameters con validación de firma y lifetime	
•	 Registro de AddAuthorization()
•	 Middleware correctamente posicionados:
		1.	CorrelationIdMiddleware
		2.	UseAuthentication() ← NUEVO
		3.	UseAuthorization() ← NUEVO
		4.	MapControllers()

**Validación:**
•	✅ Build exitoso en 30 segundos
•	✅ Sin errores de compilación
•	✅ 2 warnings de NuGet (no críticos, resolución de versiones)

**Tiempo estimado:** 1 hora

---

#### Paso 2.4: Crear política de autorización (opcional, por si se requiere) ✅ COMPLETADO

**Descripción:**
Se ha implementado la política de autorización completa para el microservicio Availability.Api:

**Extensión ClaimsPrincipalExtensions** (`src/Availability/Availability.Api/Extensions/ClaimsPrincipalExtensions.cs`):
   - ✅ Métodos helper para extraer claims del JWT de Keycloak
   - ✅ `GetUserId()` - Extrae el claim "sub"
   - ✅ `GetEmail()`, `GetName()`, `GetRoles()`
   - ✅ `HasValidUserId()`, `HasRole(string role)`
   - ✅ `GetClaimValue(string claimType)`

**Validación:**
- ✅ Compilación exitosa en 25.7 segundos
- ✅ Sin errores, 1 advertencia no crítica (NuGet)
- ✅ Extensión lista para Fase 2.5 y Fase 3

**Tiempo real:** 0.75 horas

---

#### Paso 2.5: Implementar extracción de UserId desde JWT Claims
- Crear helper o extensión para obtener `sub` (subject/UserId) del token
- Validar que el claim existe y no está vacío

    FASE 2.5 HA SIDO COMPLETADA EXITOSAMENTE ✅ ver USAGE_GUIDE_CLAIMS_EXTENSIONS.md

    ✓ La extensión ClaimsPrincipalExtensions está lista para usarse en Fase 3
    ✓ Documentación práctica y ejemplos proporcionados
    ✓ Proyecto compila sin errores
    ✓ Integración con otros componentes validada
    ✓ Plan actualizado

    PRÓXIMO PASO: FASE 3 - IMPLEMENTACIÓN DE CONTROLADORES

    En Fase 3 se crearán:
      → SeatAvailabilityController (GET /api/events/{eventId}/seats)
      → ReservationsController (POST /api/reservations, GET /api/reservations/{requestId})

    Ambos usarán los métodos de ClaimsPrincipalExtensions exactamente como se muestra
    en la documentación de Fase 2.5.

---

**Subtotal Fase 2: 2.5 horas**

---

### **FASE 3: Implementación de Controladores y Endpoints**
**Objetivo:** Crear controladores delgados que reciben DTOs, validan y delegan en casos de uso.

**Estimación:** 2.5 horas

#### Paso 3.1: Crear controlador `SeatAvailabilityController` ✅ COMPLETADO
- **Endpoint:** `GET /api/events/{eventId}/seats`
- **Autenticación:** No requerida (público)
- **Acción:** Delegar en `IGetSeatsAvailabilityUseCase`
- **Respuesta:** Lista de asientos con estado

**Carpeta:** `Availability.Api/Controllers/SeatAvailabilityController.cs`

	IGetSeatsAvailabilityUseCase.cs - Interfaz del caso de uso
	    Define el contrato para obtener disponibilidad de asientos
        Recibe eventId como int (ID externo de la API)
        Retorna Result<SeatMapDto> con el estado de los asientos
	GetSeatsAvailabilityUseCase.cs - Implementación del caso de uso
        Convierte el ID externo (int) a ID interno (Guid)
    	Delega en GetSeatMapUseCase existente
    	Maneja errores y disponibilidad de servicios
	SeatAvailabilityController.cs - Controlador REST
    	Endpoint: GET /api/events/{eventId}/seats
    	Autenticación: No requerida (público)

•	Validaciones:
	eventId debe ser > 0
	Manejo de errores (404, 503, 500)
	Logging: Con CorrelationId incluido
	Respuesta: Lista de GetSeatsResponse con SeatId, Status, EventId

**Archivos Modificados:** 
	Program.cs - Registro de dependencias
	    Agregados using para Application.Abstractions y UseCases
        Registrados servicios:
	        GetSeatMapUseCase como Scoped
    	    IGetSeatsAvailabilityUseCase como Scoped

**Tiempo real:** 0.50 horas

---

#### Paso 3.2: Crear controlador `ReservationsController` ✅ COMPLETADO

                                                                                                
1. Interfaz para Crear Reservas
•	 src/Availability/Availability.Application/Abstractions/ICreateReservationRequestUseCase.cs
      Define el contrato para crear solicitudes de reserva
      Recibe: userId, eventId, seatId (todos como Guid)
      Retorna: RequestReceivedDto (ID + estado)
                        
2. Interfaz para Consultar Estado
•	 src/Availability/Availability.Application/Abstractions/IGetReservationStatusUseCase.cs
      Define el contrato para obtener el estado de una reserva
      Recibe: requestId, userId (como Guid)
      Retorna: RequestStatusDto (estado + datos)
                      
3. Controlador de Reservas
•	 src/Availability/Availability.Api/Controllers/ReservationsController.cs
       Endpoint POST: POST /api/reservations (202 Accepted)
           Crea una nueva solicitud de reserva
           Requiere autenticación JWT ([Authorize])
           Validaciones completas
           Logging con CorrelationId
       Endpoint GET: GET /api/reservations/{requestId:guid} (200 OK)
           Consulta el estado de una reserva existente
           Requiere autenticación JWT
           Validación de propiedad (solo el dueño puede ver)
           Manejo de errores (403 Forbidden, 404 Not Found, etc)

Archivos Modificados
1. Program.cs - Registro de dependencias
          
2. Casos de Uso Vinculados a Interfaces
    CreateReservationRequestUseCase.cs ahora implementa ICreateReservationRequestUseCase
    GetRequestStatusUseCase.cs ahora implementa IGetReservationStatusUseCase

✅ Compilación Exitosa: Build completado sin errores
✅ Warnings: Solo 2 warnings de NuGet (no críticos)
✅ Tiempo de Build: 25.4 segundos


---

#### Paso 3.3: Configurar rutas y atributos globales ✅ COMPLETADO

✅ Controladores:
•	SeatAvailabilityController: Completamente implementado con validación, logging y manejo de errores
•	ReservationsController: Completamente implementado con 2 endpoints (POST para crear, GET para status)
✅ DTOs de Respuesta:
•	ErrorResponse: Contiene Code, Message, CorrelationId y Details opcional
•	ReservationResponse: Contiene RequestId, Status y Message
•	GetReservationStatusResponse: Contiene RequestId, Status, CreatedAt y UpdatedAt
✅ Manejo de Errores:
•	Todas las respuestas incluyen CorrelationId para trazabilidad
•	Códigos de error específicos (INVALID_EVENT_ID, EVENT_NOT_FOUND, etc.)
•	Logging estructurado con información de contexto
✅ Autenticación y Autorización:
•	JWT validación mediante [Authorize] en ReservationsController
•	Extracción de UserId desde JWT claims
•	Validación que UserId es un Guid válido
✅ Validaciones:
•	Validación de ModelState (DTOs incorrectos)
•	Validación de IDs > 0 (valores inválidos)
•	Validación de Guid para requestId

**Tiempo real:** 0.50 horas

### **FASE 4: Inyección de Dependencias y Manejo de Errores**
**Objetivo:** Conectar la API con los casos de uso, configurar DI y establecer estrategia de manejo de errores.


## Paso 4.1:

**SeatAvailabilityController.cs** (Endpoint GET /api/events/{eventId}/seats)
- ✅ Validación de eventId > 0 → 400 Bad Request
- ✅ Extracción de CorrelationId desde HttpContext.Items
- ✅ Llamada a IGetSeatsAvailabilityUseCase
- ✅ Mapeo SeatMapDto → List<GetSeatsResponse>
- ✅ Manejo de errores: 404 (NotFound), 503 (ServiceUnavailable), 408 (Timeout)
- ✅ Logging completo con trazabilidad

**ReservationsController.cs** (Endpoints POST + GET)
- ✅ **POST /api/reservations**
  - ✅ Validación ModelState
  - ✅ Validación eventId, seatId > 0
  - ✅ Extracción userId desde JWT claims con User.GetUserId()
  - ✅ Validación Guid.TryParse(userId)
  - ✅ Conversión int→Guid (temporal con ConvertExternalEventIdToGuid)
  - ✅ Llamada a ICreateReservationRequestUseCase
  - ✅ Retorna **202 Accepted** (no 200 OK) ← Procesamiento asincrónico
  - ✅ Manejo errores: 400, 401, 403, 404, 409, 503

- ✅ **GET /api/reservations/{requestId:guid}**
  - ✅ Validación GUID en route constraint `:guid`
  - ✅ Extracción userId desde JWT
  - ✅ Llamada a IGetReservationStatusUseCase
  - ✅ Retorna 200 OK con GetReservationStatusResponse
  - ✅ Manejo errores: 400, 401, 403, 404, 503

**Interfaces de Casos de Uso** (Already defined, Fase 3)
- ✅ IGetSeatsAvailabilityUseCase.ExecuteAsync(int eventId, CancellationToken)
- ✅ ICreateReservationRequestUseCase.ExecuteAsync(Guid userId, Guid eventId, Guid seatId, CancellationToken)
- ✅ IGetReservationStatusUseCase.ExecuteAsync(Guid requestId, Guid userId, CancellationToken)

**Utilidades & Extensiones**
- ✅ ClaimsPrincipalExtensions.GetUserId() - Extrae claim "sub" desde JWT
- ✅ ErrorResponse - Estructura estándar con code, message, correlationId, details
- ✅ CorrelationIdMiddleware - Genera UUIDs si no existen en headers

**Documentación**
- ✅ USAGE_GUIDE_CLAIMS_EXTENSIONS.md - Guía de uso de métodos de claims
- ✅ PATRONES_CLAIMS_JWT_FASE4.md - **NUEVA** - Patrones validados y checklist

### 📊 Resultados de Compilación

```
✅ Compilación correcta. 0 Errores
⚠️  1 Advertencia inofensiva (System.IdentityModel.Tokens.Jwt version mismatch)
```

---

## Paso 4.2: DTOs Especializados

**Meta:** Definir y validar DTOs especializados con data annotations  
**Estado:** ✅ **COMPLETADA** (29 de octubre de 2026)

### ✅ Objetivos Cumplidos

**DTOs de Solicitud (Requests):**
- ✅ `CreateReservationRequest.cs`
  - ✅ [Required] + [Range(1, int.MaxValue)] en EventId y SeatId
  - ✅ Mensajes de error personalizados
  - ✅ Validación automática en ModelState del controlador

**DTOs de Respuesta (Responses):**
- ✅ `GetSeatsResponse.cs` - SeatId, Status, EventId
- ✅ `ReservationResponse.cs` - RequestId, Status, Message (202 Accepted)
- ✅ `GetReservationStatusResponse.cs` - RequestId, Status, CreatedAt, UpdatedAt
- ✅ `ErrorResponse.cs` - Code, Message, CorrelationId, Details + 2 constructores

**Documentación:**
- ✅ `ESTRUCTURA_DTOS_FASE4_2.md` (376 líneas) - Especificación completa
  - ✅ Estructura de cada DTO con ejemplos
  - ✅ Data annotations y validaciones
  - ✅ Mapeos entre capas (Guid ↔ int)
  - ✅ Códigos HTTP y casos de error
  - ✅ Checklist de completitud

**Mapeos Validados:**
- ✅ CreateReservationRequest → ICreateReservationRequestUseCase
- ✅ SeatMapDto → GetSeatsResponse (con conversión Guid → int)
- ✅ RequestReceivedDto → ReservationResponse
- ✅ RequestStatusDto → GetReservationStatusResponse

**Compilación:**
- ✅ 0 Errores | 1 advertencia inofensiva (System.IdentityModel.Tokens.Jwt version mismatch)

**Estimación:** 2 horas


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

### Fase 1 ✅ (COMPLETADA)
- [x] Crear estructura de carpetas (DTOs, Middleware) ✅ **PASO 1.1**
- [x] Implementar `CorrelationIdMiddleware.cs` ✅ **PASO 1.2**
- [x] Crear clases DTO (Request, Response) ✅ **PASO 1.1**
- [x] Configurar logging base en `Program.cs` (Serilog + Paquetes NuGet) ✅ **PASO 1.3 COMPLETADO**

### Fase 2 ✅ (COMPLETADA)
- [x] Agregar paquetes NuGet JWT Bearer ✅ **PASO 2.1 COMPLETADO**
- [x] Configurar `appsettings.json` con JWT Settings ✅ **PASO 2.2 COMPLETADO**
- [x] Implementar `AddJwtBearer()` en `Program.cs` ✅ **PASO 2.3 COMPLETADO**
- [x] Registrar middleware `UseAuthentication()` y `UseAuthorization()`
- [x] Documentar extracción de UserId desde claims JWT en controlador, ver USAGE_GUIDE_CLAIMS_EXTENSIONS.md

### Fase 3 ✅ (COMPLETADA)
- [x] Crear `SeatAvailabilityController` con endpoint `GET /api/events/{eventId}/seats` ✅ **PASO 3.1**
- [x] Crear `ReservationsController` con endpoints `POST /api/reservations` y `GET /api/reservations/{requestId}` ✅ **PASO 3.2**
- [x] Aplicar atributos `[ApiController]`, `[Route]`, `[Authorize]` ✅ **PASO 3.3**
- [x] Validar `ModelState` en cada acción ✅  - Implementado en ambos controladores
- [x] Crear clase `ErrorResponse` ✅  - Clase creada con propiedades Code, Message, CorrelationId, Details


### Fase 4 🔵 (EN PROGRESO: Controladores e Integración)

#### **Fase 4.1: Implementar Controladores REST** (✅ COMPLETADA)
**Objetivo:** Crear `SeatAvailabilityController` y `ReservationsController` con lógica básica
- [x] Crear `Controllers/SeatAvailabilityController.cs` con `GET /api/events/{eventId}/seats` ✅
- [x] Crear `Controllers/ReservationsController.cs` con:
  - [x] `POST /api/reservations` (crear reserva con respuesta 202) ✅
  - [x] `GET /api/reservations/{requestId}` (consultar estado) ✅
- [x] Aplicar atributos `[ApiController]`, `[Route]`, `[Authorize]` ✅
- [x] Extraer `UserId` desde JWT claims en ambos controladores ✅
- [x] Validar `ModelState` y retornar errores 400 cuando sea necesario ✅
- [x] Llamar a casos de uso (interfaces) - sin implementación real aún ✅
- [x] Compilación validada sin errores ✅

#### **Fase 4.2: Modelos DTOs y Respuestas** (✅ **COMPLETADA**)
**Objetivo:** Definir estructuras de solicitud y respuesta con data annotations
- [x] CreateReservationRequest.cs - con [Required] y [Range] ✅
- [x] GetSeatsResponse.cs - estructura validada ✅
- [x] ReservationResponse.cs - estructura validada ✅
- [x] GetReservationStatusResponse.cs - estructura validada ✅
- [x] ErrorResponse.cs - estructura validada con 2 constructores ✅
- [x] Compilación sin errores ✅

#### **Fase 4.3: Middleware, Manejo de Errores e Inyección de Dependencias** (⏳ Pendiente)

**Objetivo:** Completar la configuración de middleware, manejo global de excepciones y optimizar inyección de dependencias

**Sub-tarea 4.3.1: Crear ExceptionHandlingMiddleware** ✅ COMPLEATADO

	Creado ExceptionHandlingMiddleware.cs
	Archivo: src/Availability/Availability.Api/Middleware/ExceptionHandlingMiddleware.cs
	Funcionalidad:
        •	Captura excepciones no controladas en el pipeline
        •	Extrae el CorrelationId del contexto HTTP para auditoría
        •	Registra el error con logging detallado usando ILogger
        •	Retorna respuesta ErrorResponse estándar con HTTP 500
        •	Evita que excepciones genéricas devuelvan HTML de ASP.NET Core
	Características:
        •	Inyección de dependencias (RequestDelegate e ILogger)
        •	Método asincrónico InvokeAsync(HttpContext context)
        •	Serialización de respuesta en JSON
        •	67 líneas de código bien documentadas

---

**Sub-tarea 4.3.2: Corregir orden de Middleware en Program.cs** ✅ COMPLEATADO

	Actualizado Program.cs con orden correcto de middleware:
	Orden implementado (CRÍTICO):
    1.	ExceptionHandlingMiddleware primero → Captura excepciones globales  
    2.	CorrelationIdMiddleware segundo → Genera/propaga IDs de correlación
    3.	UseAuthentication() → Valida JWT
    4.	UseAuthorization() → Verifica permisos

✅ Validación 4.3.1 - 4.3.2:
•	✅ Compilación exitosa (sin errores, solo 2 advertencias de NuGet normales)
•	✅ Estructura sigue patrones de diseño del proyecto
•	✅ Cumple con especificación técnica del plan
•	✅ Integración correcta en pipeline ASP.NET Core
El middleware está listo para capturar y manejar excepciones globales en la API.

---

**Sub-tarea 4.3.3: Refactorizar inyección de dependencias (Patrón DependencyInjection)** ✅ IMPORTANTE

**Problema actual (patrón no escalable):**

En `Program.cs` líneas 47-50, se registran servicios manualmente:
```csharp
builder.Services.AddScoped<GetSeatMapUseCase>();
builder.Services.AddScoped<IGetSeatsAvailabilityUseCase, GetSeatsAvailabilityUseCase>();
builder.Services.AddScoped<ICreateReservationRequestUseCase, CreateReservationRequestUseCase>();
builder.Services.AddScoped<IGetReservationStatusUseCase, GetRequestStatusUseCase>();
```

**Existe alternativa (clase DependencyInjection preparada):**

`Availability.Application/DependencyInjection.cs` define:
```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GetEventsUseCase>();
        services.AddScoped<GetSeatMapUseCase>();
        services.AddScoped<IGetSeatsAvailabilityUseCase, GetSeatsAvailabilityUseCase>();
        services.AddScoped<ICreateReservationRequestUseCase, CreateReservationRequestUseCase>();
        services.AddScoped<IGetReservationStatusUseCase, GetRequestStatusUseCase>();
        services.AddScoped<GetUserRequestsUseCase>();
        return services;
    }
}
```

**Solución (Patrón recomendado):**

Reemplazar el registro manual con llamada a método `AddApplication()`:

```csharp
// Reemplazar esto:
builder.Services.AddScoped<GetSeatMapUseCase>();
builder.Services.AddScoped<IGetSeatsAvailabilityUseCase, GetSeatsAvailabilityUseCase>();
builder.Services.AddScoped<ICreateReservationRequestUseCase, CreateReservationRequestUseCase>();
builder.Services.AddScoped<IGetReservationStatusUseCase, GetRequestStatusUseCase>();

// Con esto:
builder.Services.AddApplication();  // ← Una línea, más escalable
```

**Ventajas:**
- ✅ Escalabilidad: Agregar nuevos UseCases solo requiere editar `DependencyInjection.cs`
- ✅ Separación de responsabilidades: La configuración de Application layer vive en Application
- ✅ Reutilizable: Otros proyectos pueden importar el método

- [ ] Agregar `using Availability.Application;` en `Program.cs`
- [ ] Reemplazar 4 líneas de registro manual por: `builder.Services.AddApplication();`
- [ ] Verificar que compilación sigue siendo correcta
- [ ] Validar en runtime que inyección funciona (endpoints responden)

---

**Sub-tarea 4.3.4: Verificar mapeo de DateTime en GetReservationStatusResponse** ✅ IMPORTANTE

**Ubicación:** `Availability.Api/Controllers/ReservationsController.cs` (método GetReservationStatus)

**Problema identificado:**

```
DateTime.UtcNow → CreatedAt, UpdatedAt (TODO: Usar valores del UseCase)
```

Esto sugiere que el controlador podría estar usando `DateTime.UtcNow` en lugar de mapear desde `RequestStatusDto`.

**Verificación necesaria:**

1. ✅ El UseCase `GetRequestStatusUseCase` retorna `RequestStatusDto` con propiedades `CreatedAt` y `UpdatedAt`
2. ✅ El controlador mapea `RequestStatusDto.CreatedAt` → `GetReservationStatusResponse.CreatedAt`
3. ✅ El controlador mapea `RequestStatusDto.UpdatedAt` → `GetReservationStatusResponse.UpdatedAt`
4. ❌ NO usa `DateTime.UtcNow` (valor actual) sino valores reales del domain model

**Especificación correcta:**
```csharp
// En ReservationsController.GetReservationStatus()
var statusDto = await _getReservationStatusUseCase.ExecuteAsync(requestId, userId, cancellationToken);

// Mapeo correcto:
var response = new GetReservationStatusResponse
{
    RequestId = statusDto.RequestId.ToString("D"),  // GUID externo → string
    Status = statusDto.Status,
    CreatedAt = statusDto.CreatedAt,                // ← DEL DOMAIN, NO DateTime.UtcNow
    UpdatedAt = statusDto.UpdatedAt                 // ← DEL DOMAIN, NO DateTime.UtcNow
};
```

- [ ] Revisar controlador `ReservationsController.GetReservationStatus()`
- [ ] Confirmar que mapea `RequestStatusDto.CreatedAt` y `RequestStatusDto.UpdatedAt`
- [ ] NO usar `DateTime.UtcNow` (usar valores del UseCase)
- [ ] Si hay asignaciones de `DateTime.UtcNow`, reemplazar por valores del DTO

---

**Tiempo estimado:** 1.0 hora (0.5h middleware + 0.3h DependencyInjection + 0.2h validaciones)

**Deliverables:**
- ✅ `ExceptionHandlingMiddleware.cs` creado y registrado
- ✅ Middleware reordenado en `Program.cs`
- ✅ Inyección de dependencias refactorizada usando `AddApplication()`
- ✅ Verificación de mapeo de DateTime completada
- ✅ Compilación sin errores
- ✅ Pruebas funcionales básicas de manejo de errores

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
