# Plan de implementación — Availability · Capa Infrastructure
# Plan de implementación — Availability · Capa Infrastructure

**Proyecto:** TP Final — Sistema de reserva de asientos (grupo 5)
**Basado en:** `technical-spec.md`, `business-rules.md` y `product-brief.md` (2026-10-31)
**Responsable:** Rebeca — capa Infrastructure
**Última actualización:** 2026-11-03

## Cómo leer este plan

- El plan está organizado **por fases y dependencias**. No tiene fechas ni estimaciones de esfuerzo, porque no se definieron.
- **Una PR por fase**, de `feat/availability-rebeca` hacia `feat/availability`, revisada por otra persona del subgrupo.
- Cada tarea tiene un estado:

| Estado | Significado |
|---|---|
| **Lista** | Se puede hacer ahora. Todo lo que necesita está cerrado. |
| **Bloqueada (U-xx)** | Depende de una respuesta o decisión pendiente (sección 3). **No se avanza hasta que la confirmes**, o hasta que indiques que se avance con el valor provisorio. |
| **Pendiente de autorización** | Las dependencias técnicas están resueltas, pero la ejecución requiere autorización para producir un efecto externo. |

- La columna **Provisorio posible** muestra qué valor se usaría, tomado de las propuestas de la spec, **solo si autorizás** avanzar con él. Ese valor se marcaría como provisorio en el código y en la documentación hasta que se confirme.
- "Lista (tras T0.1)" quiere decir que la tarea depende de otra tarea del plan y no de una respuesta externa.

**Cómo me destrabás una tarea.** Escribime, por ejemplo: `T3.2 confirmada: el mensaje vive en Infrastructure/Messaging` o `T1.9: avanzá con el provisorio`. Después actualizo las tareas que dependen de ella.

---

## 1. Mapa de fases

| Fase | Contenido | PR |
|---|---|---|
| **F0** | Preparación del proyecto | `feat(availability): estructura de la capa Infrastructure` |
| **F1** | Persistencia: DbContext, entidades, migración, repositorios | `feat(availability): persistencia con PostgreSQL` |
| **F2** | Arranque: migraciones al iniciar y registro de dependencias | `feat(availability): inicialización de la base y DI` |
| **F3** | Publisher al broker | `feat(availability): publisher de solicitudes en Service Bus` |
| **F4** | Autenticación con Keycloak y cliente de Booking | `feat(availability): cliente HTTP a Booking con token` |
| **F5** | Reintentos, timeouts y logs de auditoría | `feat(availability): resiliencia y auditoría` |
| **F6** | Pruebas | `test(availability): pruebas de la capa Infrastructure` |
| **F7** | Cierre y entrega | `docs(availability): documentación y cierre` |

**Dependencias entre fases:**

```
F0 ──► F1 ──► F2
 │
 ├──► F3 ──┐
 │         ├──► F5 ──┐
 └──► F4 ──┘         ├──► F6 ──► F7
        F1, F2 ──────┘
```

- **F3 y F4 no dependen de la persistencia**, así que pueden avanzar en paralelo con F1 y F2.
- **F5** necesita el publisher (F3) y el cliente de Booking (F4), porque les agrega resiliencia y logs.
- **F6** necesita el código de las fases anteriores. Si el framework de pruebas se decide antes, las pruebas de cada fase pueden pasar a la PR de esa fase.
- **F7** cierra todo y se hace al final.

## 2. Flujo de trabajo por fase

Según la organización de Git del reparto de tareas:

1. Trabajar en `feat/availability-rebeca`, que sale de `feat/availability`.
2. Antes de abrir la PR, traer los cambios de la rama padre (`git pull origin feat/availability`) y resolver conflictos en tu rama.
3. Commits con Conventional Commits y el servicio como scope, por ejemplo `feat(availability): agregar repositorio de solicitudes`.
4. Abrir la PR hacia `feat/availability`; la revisa otra persona del subgrupo.
5. Al mergear, actualizar tu rama desde `feat/availability` para seguir con la fase siguiente.

**Excepción a "una PR por fase":** el contrato del mensaje en `/contracts` (T3.7) solo puede cambiar con una rama `chore/contracts-*` hacia `develop`, con aprobación de los dos subgrupos involucrados. Esa es una PR aparte.

La PR final de `feat/availability` hacia `develop` la abre el subgrupo completo, no vos sola.

---

## 3. Registro de desbloqueos

Cada desbloqueo es una respuesta o decisión que falta. La columna de tareas muestra qué destraba.

| ID | Qué falta | Con quién | Tareas que destraba |
|---|---|---|---|
| **U-02** | **Resuelto:** vive en `/contracts/events/ReservationRequestedMessage.cs`, namespace `Booking.Business.Messages`; Infrastructure lo compila como archivo vinculado (BR-P-05). | Feli | T3.2 |
| **U-03** | **Resuelto (2026-11-03, decisión de implementación):** `UpdateStatusAsync` lanza `KeyNotFoundException` si el `Id` no existe (BR-11). | Rebeca | T1.7 |
| **U-04** | **Resuelto (2026-11-03, decisión de Rebeca conversada con Feli):** solicitudes por `CreatedAt` descendente; eventos por `StartsAt` ascendente; asientos por `Label` ascendente. En cada lista, `Id` ascendente desempata para mantener un orden estable (BR-42). | Rebeca / Feli | T1.9 |
| **U-05** | **Resuelto:** el mensaje incluye `AmountCents` y `Currency`, no agrega `correlationId` al cuerpo y `RequestedAt` es `DateTimeOffset` (BR-P-06, BR-P-07). | Gian | T3.5, T3.6 |
| **U-06** | **Resuelto (2026-11-02)** se verificó desde una aplicación de consola externa que la suscripción `booking` existe en el topic `grupo5-booking-topic`. 
| **U-07** | **Resuelto (2026-11-03, verificado en la implementación compartida de Booking):** `200` devuelve `{ "status", "description" }`; `404` significa que Booking todavía no tiene una reserva para el `requestId`. Availability interpreta ese `404` como pendiente si la solicitud existe localmente. | Booking API | T4.7 |
| **U-08** | **Resuelto (2026-11-03):** estandarizar el header saliente como `X-Correlation-ID`. Las consultas de reserva usan `requestId`; otras consultas propagan el ID HTTP. | Santi / Rebeca | T4.8 |
| **U-09** | **Acordado e implementado (2026-11-04):** PostgreSQL 17 como `availability-db`, puerto host `5433` → contenedor `5432`, variables `AVAILABILITY_DB_NAME`, `AVAILABILITY_DB_USER`, `AVAILABILITY_DB_PASSWORD`, healthcheck `pg_isready` y dependencia saludable desde `availability-api`. | Felipe / Rebeca | T1.11, T2.4, T2.5 |
| **U-10** | `Booking__BaseUrl=http://booking:8080/` en Docker y `http://localhost:5200/` en ejecución local. Falta cablearla en la configuración de la API, que mantiene Luciana. | Felipe / Santi | T4.5, T4.10 |
| **U-11** | **Convención ** la API leerá `Keycloak__ClientSecret`, con valor `${AVAILABILITY_CLIENT_SECRET}` en Compose. Falta agregar ese mapeo cuando se incorpore el servicio de la API, responsabilidad de Luciana/Felipe. | Felipe | T4.4, T4.10 |
| **U-12** | **Decisión confirmada (2026-11-03):** dentro de Compose, el token endpoint es `http://keycloak:8081/realms/seat-reservation/protocol/openid-connect/token`; el `iss` permanece `http://localhost:8081/realms/seat-reservation`. Falta aplicar la configuración en la API. | Felipe | T4.3, T4.10 |
| **U-13** | **Convención acordada (2026-11-03):** mantener Serilog y usar logs estructurados con `Service`, `Operation`, `Event`, `CorrelationId`; agregar duración, status HTTP y excepción cuando corresponda, sin registrar secretos ni payloads sensibles. La configuración de Serilog y el middleware de API requieren integración de Luciana; Infrastructure ya registra algunos eventos estructurados. | Felipe / Rebeca | T5.5, T5.7 |
| **U-14** | **Decisión confirmada (2026-11-03):** usar `requestId` en operaciones de reserva y el ID del middleware HTTP en consultas sin `requestId`; propagarlo a Booking como `X-Correlation-ID`. El cliente de Infrastructure fija `requestId` en la consulta de estado y el handler propaga el ID HTTP para otras consultas. | Felipe / Feli / Luciana | T5.6 |
| **U-15** | Proyecto Api con el paquete `Microsoft.EntityFrameworkCore.Design` y un `Program.cs` que llame a `AddInfrastructure` y `ApplyMigrationsAsync`; coordinar además la configuración y el servicio API en Compose con Felipe. | Luciana / Felipe | T1.10, T2.3, T2.5, integración de U-09/U-11 |
| **U-16** | **Resuelto:** suite propia `Availability.Infrastructure.Tests` con xUnit; PostgreSQL real vía Testcontainers para repositorios, `HttpMessageHandler` falso para HTTP y doble de `ServiceBusSender`. | Grupo | T6.1 y todas las pruebas de F6 |
| **U-17** | **Resuelto (2026-11-03):** usar los valores por defecto de `Microsoft.Extensions.Http.Resilience` para los clientes HTTP y del SDK de Service Bus para publicaciones; no definir valores personalizados. | Grupo | T5.1, T5.3, T5.4 |
| **U-18** | **Resuelto (2026-11-03):** Service Bus usa solo los reintentos nativos del SDK; no agregar Polly ni una segunda política de reintentos al publisher. | Grupo | T5.2, T5.4 |
| **U-19** | Ubicación y formato del historial de prompts (el reparto indica `/prompts` con `Nombre_Apellido.md` y la guía `prompts/Nombre_Apellido/`) y de la carpeta de decisiones. | Grupo / Felipe | T7.2, T7.3 |
| **U-20** | Quién del subgrupo arma el diagrama de Availability, la marca de consistencia eventual y la tabla de interpretación. | Subgrupo | T7.4 |
| **U-21** | **Resuelto (2026-11-03):** se verificó que `feat/availability` existe y que Domain y Application tienen las entidades e interfaces acordadas con Feli. | Rebeca / Feli | T0.1 |

### Qué conviene cerrar primero

Ordenados por cuántas tareas destraban:

1. **U-15 (Luciana):** destraba la migración inicial y el arranque.
2. **U-16 (grupo):** definir framework y estrategia de pruebas para habilitar F6.
3. **U-19/U-20 (grupo):** acordar ubicación de documentación y responsables del diagrama para cerrar F7.

---

## 4. Fase 0 — Preparación del proyecto

**Objetivo:** tener la estructura de Infrastructure lista y compilando.
**Depende de:** U-21.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T0.1** | Verificar que `feat/availability` existe y que Domain y Application tienen las entidades (`ReservationRequest`, `Event`, `Seat`, `RequestStatus`) y las cinco interfaces acordadas con Feli. | U-21 | **Verificada** | — |
| **T0.2** | Crear `feat/availability-rebeca` desde `feat/availability`. | T0.1 | **Rama actual verificada; parte de `origin/feat/availability`** | — |
| **T0.3** | Crear la estructura de carpetas de la spec §3 y las referencias a Application y Domain. | T0.2 | **Implementada** | — |
| **T0.4** | Agregar los paquetes de la spec §3 (provider de Npgsql, `Azure.Messaging.ServiceBus`, `Microsoft.Extensions.Http.Resilience`), fijando la última versión estable compatible con .NET 10. | T0.3 | **Implementada** | — |
| **T0.5** | Crear las clases de opciones `ServiceBusOptions`, `BookingClientOptions` y `KeycloakOptions`. | T0.3 | **Implementada** | — |
| **T0.6** | Armar la lista de claves de configuración de Availability (spec §4) para pasársela a Felipe, que mantiene el compose. Las direcciones de Booking y Keycloak y el nombre del secreto ya están definidos; los valores sensibles se mantienen en `.env`. | T0.5 | **Configuración documentada; compartirla con Felipe queda pendiente** | — |

**Se considera terminada cuando:** la solución compila y la estructura coincide con la spec §3.
**PR 0:** `feat(availability): estructura de la capa Infrastructure`.

---

## 5. Fase 1 — Persistencia

**Objetivo:** modelo de datos, repositorios y migración inicial.
**Depende de:** F0.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T1.1** | Definir los datos del seed: cuántos eventos y asientos, con GUID fijos, `StartsAt` en UTC, `PriceCents` y `Currency`. Después compartirlos con quien arme las pruebas de punta a punta. | F0 | **Seed definido; falta compartir GUID con quien arme la prueba E2E** | — |
| **T1.2** | `AvailabilityDbContext` con `ApplyConfigurationsFromAssembly`. | F0 | **Implementada** | — |
| **T1.3** | Configuraciones de `ReservationRequest`, `Event` y `Seat`: claves, `ValueGeneratedNever`, conversión de `Status` a texto, índices por `UserId` y `EventId`, clave foránea, y persistencia de `Event.PriceCents` y `Event.Currency`. | T1.2 | **Implementada** | — |
| **T1.4** | Seed con `HasData`. | T1.1, T1.3 | **Implementada** | — |
| **T1.5** | `ReservationRequestRepository`: `SaveAsync`, `GetByIdAsync` (devuelve `null` si no existe) y `GetByUserIdAsync` ordenado por `CreatedAt` descendente y `Id` ascendente. | T1.3 | **Implementada** | — |
| **T1.6** | `UpdateStatusAsync`: cambia únicamente el estado de una solicitud existente. | T1.5 | **Implementada** | — |
| **T1.7** | `UpdateStatusAsync`: lanza `KeyNotFoundException` si la solicitud no existe. | T1.6, U-03 | **Implementada (2026-11-03)** | — |
| **T1.8** | `EventRepository.GetAllAsync` ordenado por `StartsAt` ascendente e `Id` ascendente; `SeatRepository.GetByEventAsync` ordenado por `Label` e `Id` ascendentes; ambos de solo lectura con `AsNoTracking`. `ExistsAsync` verifica el par evento + asiento. | T1.3 | **Implementada** | — |
| **T1.9** | Aplicar el orden definido para solicitudes, eventos y asientos, con `Id` ascendente como desempate estable. | T1.5, T1.8, U-04 | **Implementada (2026-11-03)** | — |
| **T1.10** | Generar la migración inicial `InitialCreate` con `dotnet ef`, usando el Api como proyecto de inicio. | T1.4, T1.8, U-15 | **Implementada y generada (2026-11-04)** | — |
| **T1.11** | Verificar contra el PostgreSQL del compose que se crean las tres tablas y se carga el seed. | T1.10, U-09 | **Verificada (2026-11-04):** migración aplicada; dos eventos y ocho asientos sembrados. | — |
| **T1.12** | Comentar en el código las decisiones y patrones (Repository, Fluent API, `AsNoTracking`, `ValueGeneratedNever`, UTC). | T1.5, T1.8 | **Implementada** | — |

**Se considera terminada cuando:** el código compila y los repositorios cumplen BR-01, BR-03, BR-06 a BR-14. La migración y su verificación (T1.10 y T1.11) cierran los criterios de aceptación de tablas y seed.
**PR 1:** `feat(availability): persistencia con PostgreSQL`.

---

## 6. Fase 2 — Arranque y registro de dependencias

**Objetivo:** que la app migre la base al iniciar y que la capa se registre con una sola llamada.
**Depende de:** F1.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T2.1** | `DatabaseInitializer.ApplyMigrationsAsync` (equivalente asíncrono de `Database.Migrate()`). | F1 | **Implementada** | — |
| **T2.2** | `AddInfrastructure(IServiceCollection, IConfiguration)`: opciones, `DbContext` con `ConnectionStrings:AvailabilityDb`, repositorios y `TimeProvider`. Los registros del publisher, el token y el cliente de Booking se suman en F3 y F4. | F1 | **Implementada** | — |
| **T2.3** | Coordinar con Luciana que `Program.cs` llame a `AddInfrastructure(builder.Configuration)` y a `ApplyMigrationsAsync()`, y que Api.csproj incluya `Microsoft.EntityFrameworkCore.Design`. | T2.1, T2.2, U-15 | **Implementada e integrada (2026-11-04)** | — |
| **T2.4** | Que Availability espere a que PostgreSQL esté listo: `depends_on` con `service_healthy` en el compose y reintentos de conexión en Npgsql. | U-09 | **Parcial:** Compose espera `availability-db` saludable; `EnableRetryOnFailure()` en Npgsql todavía no está configurado. | `EnableRetryOnFailure()` en las opciones de Npgsql (propuesta técnica de la spec §5.4) |
| **T2.5** | Verificar que la app migra y siembra la base al iniciar con `docker-compose up`. | T2.3, T2.4, U-09, U-15 | **Verificada (2026-11-04):** la API inicia saludable y aplica/encuentra aplicada la migración; PostgreSQL contiene dos eventos y ocho asientos. | — |

**Se considera terminada cuando:** al iniciar la app con el compose, la base queda migrada y sembrada sin pasos manuales.
**PR 2:** `feat(availability): inicialización de la base y DI`.

---

## 7. Fase 3 — Publisher al broker

**Objetivo:** publicar la solicitud en el topic con el contrato cerrado con Gian.
**Depende de:** F0. Puede avanzar en paralelo con F1 y F2.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T3.1** | Verificar que la suscripción `booking` existe en `grupo5-booking-topic`. Si no existe, los mensajes publicados se pierden sin avisar. | U-06 | **Confirmada externamente (2026-11-02)** | — |
| **T3.2** | Vincular desde `/contracts/events` el `ReservationRequestedMessage` compartido (`RequestId`, `EventId`, `SeatId`, `UserId`, `RequestedAt`, `AmountCents`, `Currency`). | U-02 | **Implementada** | — |
| **T3.3** | Registrar `ServiceBusClient` y `ServiceBusSender` como singleton, leyendo `ServiceBus:ConnectionString` y `ServiceBus:TopicName`, sin opciones de reintento. | F0 | **Implementada** | — |
| **T3.4** | `ServiceBusReservationPublisher`: mapear la solicitud y el evento (`AmountCents` = `Event.PriceCents`, `Currency` = `Event.Currency`), serializar en JSON camelCase con `ContentType: application/json`, usar `RequestId` para `MessageId` y `CorrelationId`, y lanzar `ExternalServiceUnavailableException` si falla el envío. | T3.2, T3.3 | **Implementada** | — |
| **T3.5** | Incluir en el cuerpo `AmountCents` y `Currency`; no agregar `correlationId` al JSON. | T3.4, U-05 | **Resuelta por contrato** | — |
| **T3.6** | Confirmar que el contrato define `RequestedAt` como `DateTimeOffset`. | U-05 | **Confirmada por contrato** | — |
| **T3.7** | Subir el contrato del mensaje a `/contracts/events/` con una rama `chore/contracts-*` hacia `develop`, con aprobación de los dos subgrupos. **PR aparte.** Verificar primero si ya existe. | Contrato cerrado con Gian | **Implementada; contrato confirmado en `origin/develop`** | — |
| **T3.8** | Registrar el publisher en `AddInfrastructure`. | T3.4 | **Implementada** | — |
| **T3.9** | Verificar con un consumidor de prueba sobre la suscripción `booking` que llega el JSON acordado con los identificadores correctos. | T3.1, T3.4, U-06 | **Pendiente para publicar en Service Bus** | — |
| **T3.10** | Registrar inicio, finalización y falla con correlación por `requestId`; reutilizar un sender y no ocultar errores de envío. | T3.4 | **Implementada** | — |

**Se considera terminada cuando:** el mensaje llega a la suscripción `booking` con el JSON acordado, y el `MessageId` y el `CorrelationId` coinciden con el `requestId` (BR-15 a BR-21).
**PR 3:** `feat(availability): publisher de solicitudes en Service Bus`.

---

## 8. Fase 4 — Autenticación y cliente de Booking

**Objetivo:** consultar a Booking con un token del client `availability`.
**Depende de:** F0. Puede avanzar en paralelo con F1 a F3.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T4.1** | `KeycloakTokenProvider`: `client_credentials`, caché del token y renovación 30 segundos antes del vencimiento, con un semáforo para no pedir varios tokens a la vez. | F0 | **Implementada** | — |
| **T4.2** | `BookingAuthHandler`: agrega `Authorization: Bearer` en cada llamada. | T4.1 | **Implementada** | — |
| **T4.3** | Configurar el token endpoint de Keycloak para Docker y desarrollo local, validando el issuer acordado. | U-12 | **Decisión resuelta; configuración de API pendiente con Luciana** | Docker: `http://keycloak:8081/realms/seat-reservation/protocol/openid-connect/token`; local: `http://localhost:8081/realms/seat-reservation/protocol/openid-connect/token` |
| **T4.4** | Fijar el nombre de la variable del secreto del client `availability` en el contenedor de Availability. | U-11 | **Convención resuelta; falta cablear al agregar la API al compose** | `Keycloak__ClientSecret: ${AVAILABILITY_CLIENT_SECRET}` |
| **T4.5** | Fijar `Booking:BaseUrl`. | U-10 | **Decisión resuelta; configuración de API pendiente con Luciana** | Docker: `http://booking:8080/`; local: `http://localhost:5200/` |
| **T4.6** | `BookingClient.GetOccupiedSeatIdsAsync`: `GET api/events/{eventId}/occupied-seats`, deserializa `[{ seatId, isOccupied }]` y devuelve los `seatId` con `isOccupied` en `true`; lista vacía con `200` significa "nada ocupado". Si Booking falla, lanza `ExternalServiceUnavailableException`. | T4.2 | **Implementada (2026-11-03)** | — |
| **T4.7** | `BookingClient.GetStatusByRequestIdAsync`: `GET api/reservations/by-request/{requestId}`, deserializa `{ status, description }`, devuelve `null` ante `404` (solicitud todavía no disponible en Booking) y lanza `ExternalServiceUnavailableException` ante otros errores o respuestas inválidas. | T4.6, U-07 | **Implementada (2026-11-03)** | — |
| **T4.8** | Enviar `X-Correlation-ID`: `requestId` en consultas de reserva y el ID HTTP en las consultas sin `requestId`. | T4.7, U-08 | **Implementada (2026-11-03):** el cliente fija el `requestId` para la consulta de estado y el handler propaga el ID HTTP para otras consultas | — |
| **T4.9** | Registrar en `AddInfrastructure` el `KeycloakTokenProvider`, los handlers y el cliente HTTP de Booking. | T4.2, T4.6 | **Implementada (2026-11-03)** | — |
| **T4.10** | Verificar contra Keycloak y Booking reales: el token se pide una sola vez mientras es válido y las dos consultas responden. | T4.3, T4.4, T4.5, T4.7 | **Pendiente de integrar y configurar la API en Compose (Luciana/Felipe) y de disponer de los servicios reales** | — |
| **T4.11** | Comentar las decisiones: caché del token, handler delegante, `404` como `null`, errores que se distinguen. | T4.7 | **Implementada (2026-11-03)** | — |

**Se considera terminada cuando:** el cliente obtiene un token del client `availability`, lo cachea y consulta los dos endpoints de Booking (BR-22 a BR-30).
**PR 4:** `feat(availability): cliente HTTP a Booking con token`.

---

## 9. Fase 5 — Reintentos, timeouts y auditoría

**Objetivo:** que el cliente HTTP y el publisher toleren fallas, y que cada operación deje trazas de auditoría.
**Depende de:** F3 y F4 (y de F1 para los logs del repositorio).

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T5.1** | Definir los valores de timeouts y reintentos del cliente HTTP y del publisher. | U-17 | **Resuelta (2026-11-03):** se aceptan los valores por defecto de las librerías. | — |
| **T5.2** | Confirmar si el publisher usa los reintentos del SDK o Polly. | U-18 | **Resuelta (2026-11-03):** solo reintentos nativos de Azure Service Bus, sin Polly. | — |
| **T5.3** | Agregar `AddStandardResilienceHandler()` al `BookingClient` y al `HttpClient` de Keycloak, **registrado antes** del handler de token para que cada reintento consulte el token vigente. | F4, T5.1 | **Implementada (2026-11-03)** | — |
| **T5.4** | Configurar `ServiceBusRetryOptions` en el `ServiceBusClient`. | F3, T5.1, T5.2 | **Implementada (2026-11-03):** opciones del SDK explícitas, sin personalizar sus valores por defecto y sin Polly. | — |
| **T5.5** | Aplicar la convención de logs acordada: Serilog estructurado con `Service`, `Operation`, `Event`, `CorrelationId`, duración, status HTTP y excepción según corresponda. | U-13 | **Parcial: publisher, repositorios, BookingClient y adquisición de token emiten eventos estructurados; falta configurar la salida y el middleware en API (Luciana)** | — |
| **T5.6** | Propagar la correlación HTTP de consultas sin `requestId` a Booking con `X-Correlation-ID`; las consultas de reserva usan `requestId`. | U-14 | **Implementada en Infrastructure; requiere el middleware HTTP de API para consultas sin `requestId`** | — |
| **T5.7** | Logs `Started`, `Completed` y `Failed` correlacionados en las operaciones de Infrastructure y en el pipeline HTTP; nunca registrar secretos, tokens ni connection strings. | F1, F3, F4 | **Parcial: Infrastructure registra eventos; falta el pipeline HTTP de API (Luciana)** | — |

**Se considera terminada cuando:** hay reintentos y timeouts configurados en los dos clientes, y los logs de auditoría permiten seguir un `requestId` por Availability (BR-41).
**Nota:** como los reintentos pueden duplicar un mensaje, la protección es el `MessageId` igual al `requestId` y la idempotencia de Booking por `requestId`.
**PR 5:** `feat(availability): resiliencia y auditoría`.

---

## 10. Fase 6 — Pruebas

**Objetivo:** verificar los criterios de aceptación del product brief (sección 12).
**Depende de:** F1 a F5 y de U-16.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T6.1** | Decidir con el grupo el framework de pruebas y si los repositorios se prueban contra PostgreSQL real o en memoria. Una base en memoria no reproduce algunas restricciones de PostgreSQL. | U-16 | **Resuelta:** xUnit y PostgreSQL real con Testcontainers; handlers HTTP falsos y doble de sender Service Bus. | — |

Pruebas de `Availability.Infrastructure.Tests`:

| ID | Prueba | Regla | Depende de | Estado |
|---|---|---|---|---|
| **T6.2** | `SaveAsync` con `Id` repetido falla | BR-08 | T6.1, F1 | **Implementada; pendiente de ejecutar con Docker** |
| **T6.3** | `UpdateStatusAsync` cambia el estado, lanza `KeyNotFoundException` si el Id no existe y los tres valores del enum se guardan y se leen | BR-10, BR-11 | T6.1, F1 | **Implementada; pendiente de ejecutar con Docker** |
| **T6.4** | `ExistsAsync` es `false` si el asiento no pertenece al evento | BR-01 | T6.1, F1 | **Implementada; pendiente de ejecutar con Docker** |
| **T6.5** | `GetByUserIdAsync` devuelve solo las solicitudes del usuario en orden de `CreatedAt` descendente y `Id` ascendente | BR-14, BR-42 | T6.1, F1 | **Implementada; pendiente de ejecutar con Docker** |
| **T6.6** | El seed carga al menos un evento con asientos y es reproducible | BR-04 | T6.1, F1 | **Implementada; pendiente de ejecutar con Docker** |
| **T6.7** | El publisher envía el JSON acordado con `MessageId` y `CorrelationId` iguales al `requestId` | BR-17, BR-19 | T6.1, F3 | **Implementada y verificada** |
| **T6.8** | El cliente devuelve los `seatId` con `isOccupied` en `true`, y vacío si Booking responde `200` con lista vacía (con un `HttpMessageHandler` falso) | BR-25, BR-26 | T6.1, F4 | **Implementada y verificada** |
| **T6.9** | `GetStatusByRequestIdAsync` devuelve `null` con `404` y lanza excepción ante un error | BR-27, BR-28 | T6.1, F4 | **Implementada y verificada** |
| **T6.10** | El token se pide una sola vez mientras es válido y se envía en cada llamada | BR-29, BR-30 | T6.1, F4 | **Implementada y verificada** |

**Se considera terminada cuando:** todas las pruebas pasan y cubren los criterios de aceptación del brief.
**PR 6:** `test(availability): pruebas de la capa Infrastructure`.

---

## 11. Fase 7 — Cierre y entrega

**Objetivo:** dejar la capa documentada y lista para la integración del subgrupo.
**Depende de:** F1 a F6.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T7.1** | Revisar que todas las decisiones y patrones estén justificados con comentarios en el código, como pide la consigna. | F1 a F5 | Lista (al final) | — |
| **T7.2** | Preparar tu archivo de historial de prompts con nombre y apellido, sin secretos ni credenciales. El reparto lo ubica en `/prompts` como `Nombre_Apellido.md` y la guía como la carpeta `prompts/Nombre_Apellido/`. | U-19 | **Bloqueada (U-19)** | — |
| **T7.3** | Registrar las decisiones de Infrastructure en la documentación. La guía propone `docs/decisions`, pero el reparto solo define `/docs`. | U-19 | **Bloqueada (U-19)** | — |
| **T7.4** | Aportar al diagrama de arquitectura de Availability, a la marca de consistencia eventual y a la tabla de interpretación del diagrama (trabajos transversales del subgrupo). | U-20 | **Bloqueada (U-20)** | — |
| **T7.5** | Actualizar `product-brief.md`, `business-rules.md` y `technical-spec.md` con cada pendiente que se cierre. Es una tarea continua. | Cada desbloqueo | Lista | — |
| **T7.6** | Recorrer los criterios de aceptación del brief (sección 12) y marcar cuáles se cumplen. | F1 a F6 | Lista (al final) | — |

**Se considera terminada cuando:** el código está comentado, existe el historial de prompts, la documentación está actualizada y los criterios de aceptación están verificados.
**PR 7:** `docs(availability): documentación y cierre`.

---

## 12. Resumen de estado actual

| Fase | Tareas | Terminadas | Parciales | Pendientes o bloqueadas |
|---|---:|---:|---:|---:|
| F0 | 6 | 5 | 1 | 0 |
| F1 | 12 | 11 | 1 | 0 |
| F2 | 5 | 4 | 1 | 0 |
| F3 | 10 | 9 | 0 | 1 |
| F4 | 11 | 7 | 0 | 4 |
| F5 | 7 | 4 | 3 | 0 |
| F6 | 10 | 5 | 5 | 0 |
| F7 | 6 | 0 | 1 | 5 |
| **Total** | **67** | **45** | **12** | **10** |

**Criterio del resumen:** "Parciales" incluye tareas con una parte externa o de integración todavía pendiente; "Pendientes o bloqueadas" incluye las dependientes de decisiones, responsables externos, servicios reales o cierre de fases. T0.6 y T1.1 quedan parciales hasta compartir la configuración y los GUID del seed, respectivamente. T3.7 se confirmó mergeada en `origin/develop`.

**Desbloqueos recientes:** U-03 permitió completar T1.7, U-04 T1.9, U-07 T4.7 y U-17/U-18 T5.1–T5.4. U-16 quedó resuelta con el acuerdo de xUnit, PostgreSQL vía Testcontainers, handlers HTTP falsos y doble de Service Bus. T6.7–T6.10 están verificadas; T6.2–T6.6 están implementadas, pero requieren ejecutar Docker tests. T1.10 y T1.11 quedaron completadas: se generó y aplicó `InitialCreate` en PostgreSQL 17 y se verificaron dos eventos y ocho asientos. T2.3 y T2.5 también se verificaron con la API iniciada desde Compose. T2.4 está parcial: Compose espera el healthcheck de PostgreSQL, pero Npgsql todavía no tiene `EnableRetryOnFailure()`. T4.6–T4.9 y T4.11 están implementadas. Las configuraciones de servicios externos para T4.10 y la publicación real de T3.9 siguen pendientes.

**Por dónde continuar ya mismo:** configurar el acceso de la API a Booking/Keycloak y ejecutar T4.10; coordinar autorización para la prueba real de Service Bus T3.9. Para completar F6, ejecutar T6.2–T6.6 con Docker disponible. T2.4 puede cerrarse agregando reintentos de conexión Npgsql si el grupo confirma esa decisión. F7 sigue sujeto a U-19/U-20 y al cierre de fases.
