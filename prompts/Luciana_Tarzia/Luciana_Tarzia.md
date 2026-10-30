# Prompts de Desarrollo - Luciana Tarzia
**Proyecto:** Sistema distribuido de reserva de asientos para eventos (TP Programación 4 - TUP)  
**Componente:** Availability.Api  
**Fecha de inicio:** 29 de octubre de 2026

---

## Prompt 1: Especificación y Plan de Desarrollo - Availability.Api

**Solicitado por:** Luciana Tarzia  
**Fecha:** 29 de octubre de 2026  
**Objetivo:** Estructurar un plan de desarrollo paso a paso, realista y directo al grano, para el microservicio `Availability.Api`.

### Contenido del Prompt Original:

```
Necesito que actúes como un Tech Lead Senior en .NET y me ayudes a estructurar un plan de desarrollo 
paso a paso, realista y directo al grano, para el microservicio Availability.Api de un TP universitario 
de microservicios. Crea un MD con el plan de desarrollo y la salida de este prompt para hacer seguimiento 
del mismo. No quiero que cambies código sin mi permiso. Yo voy a orquestar el desarrollo.

Quiero que te ciñas estrictamente a los requisitos del proyecto sin agregar patrones complejos innecesarios 
(como Outbox, CQRS complejo o librerías externas no solicitadas).

### 1. Rol y Contexto
- **Proyecto:** Sistema distribuido de reserva de asientos para eventos (TP Programación 4 - TUP).
- **Componente a desarrollar:** `Availability.Api` (Capa de Presentación / REST API).
- **Arquitectura del microservicio:** Clean Architecture + Dominio Anémico.
- **Responsabilidad de este componente:** Es la puerta de entrada pública. Recibe consultas de disponibilidad, 
  valida solicitudes de reserva, registra el intento, lo envía de forma asíncrona a la capa de aplicación/broker 
  y devuelve una respuesta inmediata sin esperar al cobro ni al procesamiento de `Booking`.

### 2. Definición Clara de la Funcionalidad

#### Objetivo Principal:
Permitir a los usuarios consultar asientos de un evento, solicitar la reserva de un único asiento recibiendo 
una respuesta asíncrona inmediata (202 Accepted) y consultar el estado de su solicitud.

#### Criterios de Aceptación (AC):
- **AC1 (Consulta):** `GET /api/events/{eventId}/seats` es público (sin autenticación) y retorna la lista 
  de asientos con su estado conocido.
- **AC2 (Solicitud de reserva - Alcance limitado):** `POST /api/reservations` requiere autenticación JWT. 
  Acepta **únicamente 1 asiento por reserva** (`SeatId` individual, no colecciones/listas).
- **AC3 (Respuesta asíncrona):** `POST /api/reservations` valida el modelo, extrae el `UserId` del token JWT, 
  invoca el caso de uso `CreateReservationRequestUseCase` y responde de inmediato con `HTTP 202 Accepted` 
  conteniendo `{ requestId, status: "RECEIVED" }`.
- **AC4 (Seguimiento):** `GET /api/reservations/{requestId}` permite consultar el estado actual de la solicitud 
  delegando en la capa de aplicación.
- **AC5 (Autenticación JWT):** Valida tokens emitidos por Keycloak. Si el token es inválido o falta, 
  retorna `HTTP 401 Unauthorized`.
- **AC6 (Trazabilidad y Auditoría - Punto 3f):** Incluye un Middleware de Correlation ID. Si viene el header 
  `X-Correlation-ID`, lo conserva; si no, genera un nuevo `Guid`. Debe registrar logs de auditoría al recibir 
  el request y al emitir la respuesta 202 Accepted.

#### Material de Referencia / Contratos de DTOs:

*Request POST /api/reservations:*
```json
{
  "eventId": 10,
  "seatId": 15
}
```

*Response 202 Accepted:*
```json
{
  "requestId": "a4f1-8902-4b21-9a2f",
  "status": "RECEIVED",
  "message": "Solicitud recibida correctamente"
}
```

### 3. Stack Tecnológico y Restricciones

#### Tecnologías Exactas:
- **Framework:** .NET 8 / ASP.NET Core Web API.
- **Autenticación:** `Microsoft.AspNetCore.Authentication.JwtBearer` (Keycloak).
- **Logging:** `ILogger` nativo de .NET con Serilog o Console Logger.

#### Patrones y Convenciones:
- **Clean Architecture:** `Availability.Api` solo depende de `Availability.Application` y `Availability.Domain`. 
  No conoce la base de datos ni los clientes concretos de infraestructura.
- **Controllers delgados (Thin Controllers):** Los controladores no tienen lógica de negocio ni de validación 
  compleja; solo reciben DTOs, extraen claims, invocan casos de uso y devuelven `IActionResult`.
- **Dominio Anémico:** Clases de entidad simples con getters/setters auto-implementados.

#### Lo que NO debes hacer:
- **NO** permitas listas o arreglos de asientos (`List<int> SeatIds`). Solo 1 asiento por reserva.
- **NO** agregues lógica de base de datos (EF Core) ni clientes de Azure Service Bus dentro del proyecto `Availability.Api`.
- **NO** crees patrones complejos como Transactional Outbox, CQRS con MediatR o gRPC en este microservicio.
- **NO** hagas llamadas síncronas bloqueantes a `Booking` o `Payment` para procesar el pago durante el `POST`.

### 4. Desglose del Plan de Desarrollo
Necesito que dividas el plan de trabajo en **fases lógicas y secuenciales**, estimando el tiempo en horas 
para cada una:

1. **Fase 1: Setup, DTOs y Middlewares base** (Correlation ID Middleware e ILogger).
2. **Fase 2: Configuración de Autenticación y Autorización JWT** (Program.cs e integración con Keycloak).
3. **Fase 3: Implementación de Controladores y Endpoints** (Endpoints de consulta, solicitud 202 Accepted y seguimiento).
4. **Fase 4: Inyección de Dependencias y Manejo de Errores** (Conexión de la API con los casos de uso mockeados/reales 
   e Inyección de Dependencias).

### 5. Formato de Salida Esperado
Devuélveme la respuesta estructurada de la siguiente manera:

1. **Listado paso a paso numerado** organizado por las 4 fases solicitadas.
2. **Estimación de tiempo** por cada fase y tiempo total del componente.
3. **Bloques de código de ejemplo** únicamente para las partes críticas (`CorrelationIdMiddleware`, extracción 
   de `UserId` de Claims JWT en el controller y configuración de `AddJwtBearer` en `Program.cs`).
```

### Aclaraciones Posteriores (Actualizaciones):

1. **Manejo de IDs con GUID:**
   - `RequestId`: Se genera como `Guid.NewGuid()` al crear la solicitud de reserva
   - `CorrelationId`: Se genera como `Guid.NewGuid().ToString("N")` si no viene en el header
   - `EventId` y `SeatId`: Recibidos como `int` en DTOs
   - Formato de respuesta: GUIDs como strings en formato UUID estándar

### Resultado Entregado:

**Archivo:** `PLAN_DESARROLLO_AVAILABILITY_API.md`

**Contenido principal:**
- ✅ 4 fases estructuradas con estimación de tiempo (9 horas totales)
- ✅ Paso a paso detallado con sub-fases
- ✅ Código de ejemplo para componentes críticos:
  - `CorrelationIdMiddleware.cs`
  - Configuración `AddJwtBearer()` en `Program.cs`
  - Extracción de `UserId` desde JWT claims
  - `ExceptionHandlingMiddleware.cs`
- ✅ Checklist de completación por fase
- ✅ Estructura final de archivos esperados
- ✅ Criterios de Aceptación mapeados a endpoints


---

## Prompt 2: Ejecución Fase 1.1 - Crear DTOs

**Solicitado por:** Luciana Tarzia  
**Objetivo:** Iniciar desarrollo de Fase 1, Paso 1.1 - Crear estructura de carpetas y DTOs

### Contenido del Prompt:

```
arranquemos con fase 1 del desarrollo. vamos primero con 1.1. lee md adjunto para contexto
```

### Contexto Proporcionado:
- Referencia: `PLAN_DESARROLLO_AVAILABILITY_API.md`
- Expectativa: Crear 4 DTOs con estructura de carpetas Requests/Responses

### Resultado Entregado:

**Archivos Creados:**
- ✅ `src/Availability/Availability.Api/DTOs/Requests/CreateReservationRequest.cs`
- ✅ `src/Availability/Availability.Api/DTOs/Responses/ReservationResponse.cs`
- ✅ `src/Availability/Availability.Api/DTOs/Responses/GetSeatsResponse.cs`
- ✅ `src/Availability/Availability.Api/DTOs/Responses/GetReservationStatusResponse.cs`

**Propiedades implementadas:**
- `CreateReservationRequest`: `EventId` (int), `SeatId` (int)
- `ReservationResponse`: `RequestId` (string), `Status` (string), `Message` (string)
- `GetSeatsResponse`: `SeatId` (int), `Status` (string), `EventId` (int)
- `GetReservationStatusResponse`: `RequestId` (string), `Status` (string), `CreatedAt` (DateTime), `UpdatedAt` (DateTime)

**Validación:** Compilación exitosa sin errores.

---

## Prompt 3: Actualización Plan - Fase 1.1 Completada

**Solicitado por:** Luciana Tarzia  
**Objetivo:** Registrar completación de Fase 1.1 en el plan de desarrollo

### Contenido del Prompt:

```
actualiza el plan de desarrollo con el checklist de fase 1.1 completado y hace un muy pequeño resumen 
de los cambios
```

### Cambios Realizados en PLAN_DESARROLLO_AVAILABILITY_API.md:

- ✅ Agregado checkmark `✅ COMPLETADO` en Paso 1.1
- ✅ Actualizado estado: "DTOs a crear" → "DTOs creados"
- ✅ Agregado resumen conciso: "Creadas 4 clases DTO con propiedades documentadas, estructura de carpetas organizadas por tipo de transferencia (Requests/Responses). Compilación exitosa."
- ✅ Cambio de métrica: "Tiempo estimado" → "Tiempo real: 0.5 horas"

