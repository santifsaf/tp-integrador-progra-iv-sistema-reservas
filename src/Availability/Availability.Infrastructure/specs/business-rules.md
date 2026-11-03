# Reglas de negocio — Availability · Capa Infrastructure
# Reglas de negocio — Availability · Capa Infrastructure

**Proyecto:** TP Final — Sistema de reserva de asientos (grupo 5)
**Basado en:** `product-brief.md` (última actualización 2026-10-31)
**Responsable:** Rebeca — capa Infrastructure

## Cómo leer este documento

- Cada regla tiene un identificador `BR-XX`, un enunciado verificable y su origen en el product brief (`§` es la sección y `RF` el requisito funcional).
- **Cerrada** significa acordada con Feli, Gian o Santi. Las reglas **pendientes** están al final, con el mismo formato `BR-P-XX`, y pasan a cerradas cuando se confirman.
- Una regla de "Infrastructure" describe lo que esta capa debe cumplir; las decisiones de negocio de otras capas o servicios se listan como contexto cuando condicionan a Infrastructure.

---

## 1. Catálogo de eventos y asientos

| ID | Regla | Origen |
|---|---|---|
| **BR-01** | Un asiento **existe** si hay un `Seat` con ese `Id` cuyo `EventId` coincide con el evento indicado. Se verifica el par evento + asiento, nunca el `Id` del asiento por separado. | §7.1 RF-5 |
| **BR-02** | Todo `Seat` pertenece a exactamente un `Event` (clave foránea `Seat.EventId`). | §5 |
| **BR-03** | El catálogo es de **solo lectura**: Infrastructure no ofrece operaciones para crear, modificar ni borrar eventos o asientos. | §7.1 RF-4 |
| **BR-04** | El catálogo se carga con datos sembrados (seed) definidos por Availability, con GUID fijos y reproducibles. | §7.1 RF-4, §9 |
| **BR-05** | Que un asiento exista en el catálogo **no garantiza** que esté libre: la disponibilidad la decide Booking. | §10 |
| **BR-05a** | Cada evento tiene un precio expresado en centavos (`PriceCents`) y una moneda (`Currency`). Infrastructure persiste ambos valores como parte del catálogo local. | §5 |

## 2. Solicitudes de reserva

| ID | Regla | Origen |
|---|---|---|
| **BR-06** | El `Id` de la solicitud (el `requestId`) lo genera Application. Infrastructure no lo genera ni lo modifica. | §7.1 RF-2 |
| **BR-07** | `CreatedAt` lo genera Application y siempre está en UTC. | §7.1 RF-2 |
| **BR-08** | No pueden existir dos solicitudes con el mismo `Id`. | §5 |
| **BR-09** | `SaveAsync` persiste la solicitud tal como la recibe: no completa datos ni aplica reglas de negocio. | §7.1 RF-3 |
| **BR-10** | Los estados válidos son `Received`, `Published` y `PublishFailed`. El flujo es `Received` al guardar y luego `Published` o `PublishFailed` tras intentar publicar. | §5, §9, Feli (2026-11-02) |
| **BR-11** | `UpdateStatusAsync` modifica únicamente el estado de una solicitud existente y lanza `KeyNotFoundException` si el `Id` no existe. | §7.1 RF-3, decisión de implementación (2026-11-03) |
| **BR-12** | Cada operación de escritura del repositorio guarda sus cambios por sí misma. | §7.1 RF-3 |
| **BR-13** | `GetByIdAsync` devuelve `null` si la solicitud no existe. | §7.1 RF-3 |
| **BR-14** | `GetByUserIdAsync` devuelve únicamente las solicitudes del usuario indicado. | §7.1 RF-3 |

## 3. Publicación al broker

| ID | Regla | Origen |
|---|---|---|
| **BR-15** | Las solicitudes se publican en el topic `grupo5-booking-topic`; la suscripción de Booking se llama `booking`. | §7.2 RF-8, §8.1 |
| **BR-16** | El mensaje es JSON en UTF-8, con nombres en camelCase y `ContentType: application/json`. | §7.2 RF-9 |
| **BR-17** | El cuerpo del mensaje contiene exactamente `requestId`, `eventId`, `seatId`, `userId` y `requestedAt`. No lleva un `correlationId` aparte. | §8.1 |
| **BR-18** | `requestId` toma el valor de `ReservationRequest.Id` y `requestedAt` el de `CreatedAt`. | §8.1 |
| **BR-19** | En el sobre del mensaje, `MessageId` y `CorrelationId` son iguales al `requestId`. | §7.2 RF-10 |
| **BR-20** | El `requestId` es único y estable en reintentos: reenviar la misma solicitud nunca cambia su identificador. | §7.2 RF-11 |
| **BR-21** | Si la publicación falla, el publisher lanza una excepción clara y no oculta el error. | §7.2 RF-12 |
| **BR-21a** | Si falla la publicación, Application persiste `PublishFailed` y devuelve `AppError.Unavailable`. | Feli (2026-11-02) |

## 4. Consulta a Booking

| ID | Regla | Origen |
|---|---|---|
| **BR-22** | Booking es la única fuente de verdad de la ocupación. Availability no decide si un asiento está ocupado ni confirma reservas. | §1, §9 |
| **BR-23** | Un asiento está **ocupado** si tiene una reserva `HELD` o `CONFIRMED` en Booking. Con `REJECTED` o `EXPIRED` queda libre. | §8.2 |
| **BR-24** | `GET /api/events/{eventId}/occupied-seats` devuelve **solo** los asientos ocupados, con la forma `[{ "seatId", "isOccupied" }]`. | §8.2 |
| **BR-25** | Si el evento no tiene reservas, Booking responde `200` con lista vacía, y eso significa "ningún asiento ocupado". | §8.2, §7.3 RF-16 |
| **BR-26** | `GetOccupiedSeatIdsAsync` devuelve el conjunto de `seatId` cuyo `isOccupied` es `true`. | §7.3 RF-16 |
| **BR-27** | `GetStatusByRequestIdAsync` devuelve `null` cuando Booking responde `404`, que significa que todavía no existe una reserva procesada para la solicitud; Availability lo presenta como pendiente si la solicitud existe localmente. | §7.3 RF-15, contrato de Booking verificado (2026-11-03) |
| **BR-28** | Ante cualquier error distinto de `404`, o ante una caída de Booking, el cliente lanza `Availability.Application.Common.ExternalServiceUnavailableException`. El `404` de la consulta de estado se interpreta como ausencia de la reserva y devuelve `null`. | §7.3 RF-15 |
| **BR-45** | `GetStatusByRequestIdAsync` deserializa la respuesta `200` de Booking con las propiedades `status` y `description`, correspondientes a `BookingReservationStatusDto(Status, Description)`; `description` puede ser `null`. | Contrato de Booking verificado (2026-11-03) |

## 5. Autenticación entre servicios

| ID | Regla | Origen |
|---|---|---|
| **BR-29** | Toda llamada de Availability a Booking lleva un token `Authorization: Bearer` obtenido por `client_credentials` del servidor de autorización. | §4, §7.3 RF-14 |
| **BR-30** | El token se cachea hasta su vencimiento; no se pide uno nuevo en cada llamada. | §7.3 RF-14 |
| **BR-31** | La connection string del broker, las credenciales y los secretos nunca se suben al repositorio; se leen de variables de entorno y `.env.example` no contiene valores reales. | §4 |

## 6. Consistencia entre servicios

| ID | Regla | Origen |
|---|---|---|
| **BR-32** | Availability responde `202 Accepted` antes de que Booking procese la solicitud. Ese intervalo es de **consistencia eventual** y se marca en el diagrama y en el código. | §10 |
| **BR-33** | Booking no mantiene catálogo de eventos ni de asientos; confía en que Availability verificó la existencia del asiento. | §8.2, §9 |
| **BR-34** | Infrastructure solo trae datos crudos del catálogo y de Booking. La combinación (mapa de asientos con su estado) la hace un caso de uso de Application. | §7.4 |

## 7. Arquitectura y datos

| ID | Regla | Origen |
|---|---|---|
| **BR-35** | Infrastructure implementa las interfaces que declara Application; Application y Domain no conocen a Infrastructure. | §4, §6 |
| **BR-36** | Las entidades de Domain son anémicas: solo datos, sin lógica de negocio. | §4 |
| **BR-37** | La configuración de las tablas se hace con Fluent API en Infrastructure, no con atributos en las entidades. | §7.1 RF-1 |
| **BR-38** | Todos los identificadores son `Guid` y todas las fechas se guardan en UTC. | §4 |
| **BR-39** | Availability usa su propia base PostgreSQL y ningún otro servicio lee de ella. | §4 |
| **BR-40** | Una sola persona genera y mantiene las migraciones de Availability. | §7.1 RF-7 |

## 8. Auditoría

| ID | Regla | Origen |
|---|---|---|
| **BR-41** | Cada operación registra inicio, resultado y fin (`Started`, `Completed`, `Failed`) con una correlación estable: `requestId` para el flujo de reserva y el ID HTTP para consultas sin `requestId`. | §12, decisión aceptada (2026-11-03) |

## 9. Orden de resultados

| ID | Regla | Origen |
|---|---|---|
| **BR-42** | `GetByUserIdAsync` ordena por `CreatedAt` descendente; `EventRepository.GetAllAsync` por `StartsAt` ascendente; y `SeatRepository.GetByEventAsync` por `Label` ascendente. En los tres casos, `Id` ascendente desempata para garantizar un orden estable. | Decisión de Rebeca conversada con Feli (2026-11-03) |
| **BR-43** | Los logs estructurados usan Serilog con los campos `Service`, `Operation`, `Event` y `CorrelationId`; incluyen duración, status HTTP y excepción cuando corresponda. No registran secretos, tokens, connection strings ni payloads sensibles. | Decisión aceptada (2026-11-03) |
| **BR-44** | Las llamadas a Booking usan `X-Correlation-ID`: `requestId` para operaciones de reserva y el ID recibido o generado por el middleware HTTP para consultas sin `requestId`. | Decisiones aceptadas (2026-11-03) |

---

## Reglas pendientes de confirmar

Estas reglas todavía no están cerradas. Cuando se confirmen, pasan a las secciones anteriores con un `BR-XX` definitivo.

| ID | Regla propuesta | Con quién | Origen |
|---|---|---|---|
| **BR-P-04** | Si Booking no responde, Application decide qué devolver; el cliente solo lanza la excepción. | Feli | §11 |
| **BR-P-05** | `ReservationRequestedMessage` vive en Infrastructure (carpeta `Messaging`), por ser el contrato de transporte. | Feli | §11 |
| **BR-P-06** | El sobre del mensaje puede llevar propiedades extra (como `Subject` con el tipo de evento) si Booking las necesita. | Gian | §11 |
| **BR-P-07** | `requestedAt` se deserializa como `DateTimeOffset` en el consumidor de Booking. | Gian | §11 |
