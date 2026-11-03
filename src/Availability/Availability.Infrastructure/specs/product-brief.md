# Product Brief — Availability · Capa Infrastructure
# Product Brief — Availability · Capa Infrastructure

**Proyecto:** TP Final — Sistema de reserva de asientos (grupo 5)
**Componente:** Availability (Clean Architecture, dominio anémico)
**Responsable de este documento:** Rebeca — capa Infrastructure
**Última actualización:** 2026-10-31
**Estado:** contratos cerrados con Feli (Domain/Application) y Gian (broker); pendientes puntuales con Santi (endpoints de Booking). Ver sección 9.

---

## 1. Contexto

El sistema permite consultar y reservar asientos para eventos. Muchas personas pueden intentar reservar el mismo asiento a la vez, y solo una reserva puede ocuparlo. El sistema se compone de microservicios (Availability, Booking, Payment, Notifications), un broker de mensajes (Azure Service Bus) y un servidor de autorización (Keycloak).

**Availability** es la puerta de entrada pública:

- Muestra los asientos de un evento (consulta pública, sin login).
- Recibe solicitudes de reserva de usuarios autenticados, las registra y las publica en el broker.
- Responde `202 Accepted` con un `requestId`, sin esperar el resultado.
- Permite consultar el estado de una solicitud, que obtiene de Booking.

Availability **no** retiene ni confirma asientos, ni cobra. Esa responsabilidad es de Booking y Payment.

## 2. Objetivo de este documento

Definir qué debe construir la **capa Infrastructure** de Availability, qué contratos tiene con el resto del sistema y cómo se verifica que está terminada.

## 3. Alcance de la capa Infrastructure

Según el reparto de tareas, Infrastructure **implementa las interfaces que declara Application** (Feli):

| Responsabilidad | Descripción |
|---|---|
| **SQL de Availability** | Base PostgreSQL propia con solicitudes de reserva y catálogo de eventos y asientos |
| **Publisher al broker** | Publicar el mensaje de solicitud de reserva en Azure Service Bus |
| **Cliente HTTP a Booking con token** | Consultar a Booking el estado de una solicitud y la ocupación de asientos, autenticándose con el servidor de autorización |

### Fuera de alcance

- Casos de uso, entidades, DTO y reglas de negocio (Domain/Application, Feli).
- Endpoints, validación del JWT del usuario y logs de la API (capa de presentación, Luciana).
- Decidir si un asiento está ocupado o si una reserva se confirma (Booking).
- Configuración de Keycloak y del docker-compose (Felipe) y creación de los recursos de Azure (profesor).

## 4. Arquitectura y restricciones

- **Clean Architecture:** las dependencias apuntan hacia el núcleo. Infrastructure referencia Application y Domain; ellos no conocen a Infrastructure.
- **Dominio anémico:** las entidades solo tienen datos. La lógica vive en los casos de uso de Application.
- **Microservicios:** cada servicio tiene su propia base y ningún servicio lee la base de otro.
- **Base de datos:** PostgreSQL, con el provider `Npgsql.EntityFrameworkCore.PostgreSQL`, corriendo como contenedor en el docker-compose.
- **Identificadores:** todos son `Guid`. Las fechas se guardan en UTC.
- **Autenticación entre servicios:** OAuth 2.0 `client_credentials` contra Keycloak.
- **Secretos:** la connection string del broker y las credenciales nunca se suben al repositorio (variables de entorno y `.env.example` sin valores).

Cada decisión o patrón aplicado se justifica con comentarios en el código, como pide la consigna.

## 5. Modelo de datos (entidades de Domain acordadas con Feli)

```csharp
public class ReservationRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid EventId { get; set; }
    public Guid SeatId { get; set; }
    public RequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

```csharp
namespace Availability.Domain.Entities;

public class Event
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public long PriceCents { get; set; }
    public string Currency { get; set; } = string.Empty;
}
```

```csharp
public class Seat
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string Label { get; set; } = string.Empty;
}
```

```csharp
namespace Availability.Domain.Enums;

public enum RequestStatus
{
    Received,
    Published,
    PublishFailed
}
```

`Status` se guarda como texto legible (por ejemplo `"Received"`) mediante una conversión en la configuración de EF Core.

Tablas resultantes:

| Tabla | Campos | Restricciones |
|---|---|---|
| `ReservationRequests` | `Id`, `UserId`, `EventId`, `SeatId`, `Status`, `CreatedAt` | `Id` es clave primaria (equivale al `requestId`); índice por `UserId` para listar solicitudes de un usuario |
| `Events` | `Id`, `Name`, `StartsAt`, `PriceCents`, `Currency` | Clave primaria `Id` |
| `Seats` | `Id`, `EventId`, `Label` | Clave primaria `Id`; clave foránea a `Events`; índice por `EventId` |

El `Id` de `ReservationRequest` es el `requestId` que viaja por todo el sistema.

## 6. Interfaces que implementa Infrastructure (definidas por Application)

```csharp
public interface IReservationRequestRepository
{
    Task SaveAsync(ReservationRequest request, CancellationToken ct);
    Task UpdateStatusAsync(Guid id, RequestStatus status, CancellationToken ct);
    Task<ReservationRequest?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ReservationRequest>> GetByUserIdAsync(Guid userId, CancellationToken ct);
}

public interface IEventRepository
{
    Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken ct);
    Task<Event?> GetByIdAsync(Guid eventId, CancellationToken ct);
}

public interface ISeatRepository
{
    Task<IReadOnlyList<Seat>> GetByEventAsync(Guid eventId, CancellationToken ct);
    Task<bool> ExistsAsync(Guid eventId, Guid seatId, CancellationToken ct);
}

public interface IReservationPublisher
{
    Task PublishAsync(ReservationRequest request, CancellationToken ct);
}

public interface IBookingClient
{
    Task<IReadOnlySet<Guid>> GetOccupiedSeatIdsAsync(Guid eventId, CancellationToken ct);
    Task<BookingReservationStatusDto?> GetStatusByRequestIdAsync(Guid requestId, CancellationToken ct);
}
```

DTO que usa el cliente de Booking (namespace `Availability.Application.Dtos`):

```csharp
public record BookingReservationStatusDto(string Status, string? Description);
```

Los demás DTO de Application (`EventSummaryDto`, `RequestReceivedDto`, `RequestStatusDto`, `SeatMapDto`, `SeatStatusDto`, `UserRequestSummaryDto`) los arman los casos de uso de Feli y no forman parte del trabajo de Infrastructure.

Nombres propuestos para las implementaciones: `ReservationRequestRepository`, `EventRepository`, `SeatRepository`, `ServiceBusReservationPublisher` y `BookingClient`.

## 7. Requisitos funcionales

### 7.1 SQL de Availability

**RF-1.** `AvailabilityDbContext` con configuración mediante Fluent API (no atributos en las entidades), en clases separadas por entidad.

**RF-2.** `Id` de `ReservationRequest` sin generación automática en la base: lo genera Application. `CreatedAt` también lo genera Application y siempre es UTC.

**RF-3.** `ReservationRequestRepository`:
- `SaveAsync` inserta la solicitud y guarda los cambios en el mismo método. Solo persiste lo que recibe.
- `UpdateStatusAsync` cambia el estado de una solicitud existente. Los estados posibles son `Received` (al guardar), `Published` y `PublishFailed` (después de intentar publicar en el broker).
- `GetByIdAsync` devuelve `null` si no existe.
- `GetByUserIdAsync` devuelve las solicitudes del usuario ordenadas por `CreatedAt` descendente y `Id` ascendente.

**RF-4.** `EventRepository` y `SeatRepository` son de **solo lectura** y usan `AsNoTracking()`. El catálogo se carga con datos sembrados (seed) definidos por Availability. `EventRepository.GetAllAsync` ordena eventos por `StartsAt` ascendente y `Id` ascendente.

Cada evento persiste su precio en centavos (`PriceCents`) y su moneda (`Currency`) como parte del catálogo local.

**RF-5.** `ISeatRepository.ExistsAsync` comprueba el **par** evento + asiento, no el `Id` del asiento por separado.

**RF-6.** `GetByEventAsync` devuelve los asientos de un evento ordenados por `Label` ascendente e `Id` ascendente.

**RF-7.** Migración inicial generada y mantenida por una sola persona (Rebeca) para evitar conflictos.

### 7.2 Publisher al broker

**RF-8.** `IReservationPublisher` se implementa con `Azure.Messaging.ServiceBus` y envía al topic `grupo5-booking-topic`. La suscripción de Booking se llama `booking`.

**RF-9.** El publisher convierte `ReservationRequest` en el mensaje acordado con Gian (sección 8.1) y lo serializa como JSON en UTF-8, con nombres en camelCase y `ContentType: application/json`.

**RF-10.** En el `ServiceBusMessage`, `MessageId` y `CorrelationId` se completan con el `requestId`, como sugirió Gian. Así el broker y los logs usan el mismo identificador.

**RF-11.** El `requestId` es único y **estable en reintentos**, para que Booking aplique idempotencia.

**RF-12.** Si la publicación falla, el publisher lanza una excepción clara y Application decide qué hacer. El estado `PublishFailed` permite que la solicitud quede registrada como no publicada (ver sección 11 para confirmar el flujo con Feli).

Las fallas de Azure Service Bus y Booking se representan mediante `Availability.Application.Common.ExternalServiceUnavailableException`, para que Application pueda capturarlas sin referenciar a Infrastructure.

### 7.3 Cliente HTTP a Booking con token

**RF-13.** Un `HttpClient` tipado, con la dirección de Booking en configuración, implementa `IBookingClient`:

| Método | Endpoint de Booking |
|---|---|
| `GetStatusByRequestIdAsync` | `GET /api/reservations/by-request/{requestId}` (forma de la respuesta a confirmar con Santi) |
| `GetOccupiedSeatIdsAsync` | `GET /api/events/{eventId}/occupied-seats` (cerrado con Santi) |

**RF-14.** Un `DelegatingHandler` obtiene el token por `client_credentials`, lo cachea hasta que venza y lo agrega como `Authorization: Bearer` en cada llamada.

**RF-15.** `GetStatusByRequestIdAsync` devuelve `null` ante un `404`. Ante cualquier otro error o caída de Booking, el cliente lanza una excepción clara, distinguiendo "no existe" de "falló".

**RF-16.** `GetOccupiedSeatIdsAsync` deserializa la respuesta de Booking (`[{ "seatId", "isOccupied" }]`) y devuelve un conjunto con los `seatId` cuyo `isOccupied` es `true`. Una lista vacía (`200`) significa que no hay nada ocupado.

**RF-17.** Cada llamada a Booking envía `X-Correlation-ID`: usa `requestId` en el flujo de reserva y el ID recibido o generado por el middleware HTTP en consultas sin `requestId`.

### 7.4 Quién combina catálogo y ocupación

Infrastructure solo trae datos crudos de cada fuente. **La combinación del catálogo local con la ocupación de Booking** (el mapa de asientos con su estado) la hace un caso de uso de Application, de modo que Infrastructure siga siendo un adaptador sin lógica de negocio.

## 8. Contratos

### 8.1 Mensaje de solicitud de reserva (Availability → Booking). Cerrado con Gian

- Topic: `grupo5-booking-topic`. Suscripción: `booking`.
- Formato: JSON, camelCase.
- No hay un campo `correlationId` aparte: el correlation id **es** el `requestId`.

```csharp
namespace Booking.Business.Messages;

public sealed record ReservationRequestedMessage(
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    Guid UserId,
    DateTimeOffset RequestedAt);
```

Ejemplo serializado:

```json
{
  "requestId": "3f2b8c1e-7a4d-4e9b-9c55-1d2a6b7e8f90",
  "eventId": "9d1c4a77-2b3e-4f10-8a6d-5e7f0c1b2a34",
  "seatId": "c5e8f2a1-6b7d-4c3e-9f10-8a2b4d6e7c90",
  "userId": "7e4a9b12-3c5d-4e6f-8a1b-2c3d4e5f6a78",
  "requestedAt": "2026-10-28T02:00:00Z"
}
```

Correspondencia con la entidad: `RequestId` = `Id`, `RequestedAt` = `CreatedAt`.
El contrato vive en el namespace `Booking.Business.Messages`.

El contrato se versiona en `/contracts/events/` mediante una rama `chore/contracts-*` y un PR aprobado por los dos subgrupos.

### 8.2 Endpoints de Booking que consume Availability. Parcialmente cerrado con Santi

Confirmado:

- Booking no mantiene tabla de eventos ni de asientos; la ocupación de un asiento se deduce de sus reservas.
- Un asiento está **ocupado** si tiene una reserva `HELD` o `CONFIRMED`. Con `REJECTED` o `EXPIRED` el asiento queda libre.
- Nunca puede haber dos reservas `HELD` o `CONFIRMED` sobre el mismo asiento. La exclusividad la resuelve Booking en SQL, con transacciones y operaciones condicionales.

Endpoint de ocupación, **cerrado**:

- `GET /api/events/{eventId}/occupied-seats` devuelve **solo los asientos ocupados**.
- Respuesta: `[{ "seatId": "...", "isOccupied": true }]`.
- Si el evento no tiene reservas, responde `200` con lista vacía.

Pendiente de confirmar: forma del JSON de la consulta de estado, códigos de error y qué devuelve cuando Booking todavía no procesó la solicitud (ver sección 11).

## 9. Decisiones tomadas

| Decisión | Detalle |
|---|---|
| Base de datos | PostgreSQL, una base por servicio |
| Identificadores | `Guid` en todo el sistema; la propiedad se llama `Id` en las tres entidades |
| Verificación de existencia | Catálogo local de eventos y asientos en Availability (`ISeatRepository.ExistsAsync`) |
| Catálogo | Solo lectura, con seed definido por Availability |
| `Id` y `CreatedAt` de la solicitud | Los genera Application; Infrastructure solo persiste |
| Broker | Azure Service Bus, recursos creados por el profesor; topic `grupo5-booking-topic`, suscripción `booking` |
| Formato del mensaje | JSON camelCase, sin `correlationId` aparte; `MessageId` y `CorrelationId` del sobre = `requestId` |
| Fuente de verdad de la ocupación | Booking, a partir de reservas `HELD` o `CONFIRMED` |
| Booking y el catálogo | Booking no tiene tabla de eventos ni de asientos; confía en que Availability verificó la existencia |
| Combinación de catálogo y ocupación | Caso de uso en Application |
| Estados de la solicitud | `RequestStatus`: `Received`, `Published` y `PublishFailed`, guardado como texto |
| Endpoint de ocupación de Booking | `GET /api/events/{eventId}/occupied-seats`: solo asientos ocupados, `[{ "seatId", "isOccupied" }]`; lista vacía con `200` si no hay reservas |

## 10. Consistencia eventual

Existe un intervalo entre el `202 Accepted` de Availability y el procesamiento de Booking, en el que Availability ya aceptó la solicitud pero Booking todavía no. Además, que un asiento exista en el catálogo no garantiza que esté libre: Booking puede rechazar la solicitud. Este punto se marca en el diagrama de Availability y se comenta en el código.

## 11. Preguntas abiertas

**Con Feli (Application):**

- Confirmar el flujo esperado de estados: `Received` al guardar, y `Published` o `PublishFailed` después de intentar publicar. Y qué debe hacer `UpdateStatusAsync` si el `Id` no existe (propuesta: lanzar una excepción).
- Qué responde la API al usuario si la publicación falla, y qué hace Application si Booking no responde.
- `ReservationRequestedMessage` vive en el namespace compartido `Booking.Business.Messages`.

**Con Gian (Booking, broker):**

- Si necesita alguna propiedad extra en el sobre, como `Subject` con el tipo de evento.
- Confirmar que el consumidor deserializa `requestedAt` como `DateTimeOffset`.

**Con Santi (Booking, endpoints):**

- **Resuelto (2026-11-03, verificado en la implementación compartida de Booking):** `GET /api/reservations/by-request/{requestId}` devuelve `{ status, description }`, donde `description` puede ser `null`; si Booking aún no tiene la reserva, responde `404` y Availability la interpreta como pendiente cuando la solicitud existe localmente.
- Token esperado, URL de Booking en el compose y uso del header de correlación.

**Con Felipe (Auth e infraestructura):**

- Client de Availability en Keycloak y cómo se obtiene el token.
- Variable de entorno para la connection string y servicio de Postgres en el compose.
- Datos de prueba: como Booking no tiene catálogo, el seed solo vive en Availability, pero los GUID de eventos y asientos deben compartirse con quien arme las pruebas de punta a punta.

## 12. Criterios de aceptación

- [ ] La solución compila y la migración inicial crea las tres tablas en PostgreSQL.
- [ ] El seed carga al menos un evento con varios asientos y es reproducible.
- [ ] `SaveAsync` con un `Id` repetido falla por la restricción de clave primaria.
- [ ] `UpdateStatusAsync` cambia el estado de una solicitud existente y los tres valores de `RequestStatus` se guardan y se leen correctamente.
- [ ] `ExistsAsync` devuelve `false` para un asiento que no pertenece al evento indicado.
- [ ] `GetByUserIdAsync` devuelve solo las solicitudes del usuario indicado.
- [ ] El publisher envía al topic el JSON acordado, con `MessageId` y `CorrelationId` iguales al `requestId`, verificable con un consumidor de prueba sobre la suscripción `booking`.
- [ ] El cliente de Booking obtiene un token, lo cachea y lo envía en cada llamada.
- [ ] `GetOccupiedSeatIdsAsync` devuelve los `seatId` con `isOccupied` en `true` y un conjunto vacío cuando Booking responde `200` con lista vacía.
- [ ] `GetStatusByRequestIdAsync` devuelve `null` ante `404`; ante un error de Booking lanza `ExternalServiceUnavailableException`.
- [ ] Hay logs estructurados con Serilog y los campos `Service`, `Operation`, `Event` (`Started`, `Completed`, `Failed`) y `CorrelationId`; incluyen duración, status HTTP y excepción cuando corresponda, sin registrar secretos ni payloads sensibles.
- [ ] No hay secretos en el repositorio; existe un `.env.example`.
- [ ] Hay pruebas de los repositorios, del publisher y del cliente HTTP (con un handler falso).
- [ ] Las decisiones y patrones están comentados en el código y el historial de prompts está en `/prompts`.

## 13. Estructura de la capa

```
Availability.Infrastructure/
├── Persistence/
│   ├── AvailabilityDbContext.cs
│   ├── Configurations/      # una clase por entidad (Fluent API)
│   └── Migrations/
├── Repositories/            # solicitudes, eventos y asientos
├── Messaging/               # publisher a Azure Service Bus y mensaje
├── Http/                    # cliente de Booking y handler del token
└── DependencyInjection.cs   # AddInfrastructure(...)
```

## 14. Organización de trabajo

- Rama personal: `feat/availability-rebeca`, que sale de `feat/availability`. PR hacia la rama del subgrupo, con revisión de otra persona.
- Commits con Conventional Commits y el servicio como scope, por ejemplo `feat(availability): agregar publisher de solicitudes`.
- Cambios en `/contracts` solo mediante `chore/contracts-*` con aprobación de los dos subgrupos.
