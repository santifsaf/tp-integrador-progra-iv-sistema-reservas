# Plan de implementación — Availability · Capa Infrastructure
# Plan de implementación — Availability · Capa Infrastructure

**Proyecto:** TP Final — Sistema de reserva de asientos (grupo 5)
**Basado en:** `technical-spec.md`, `business-rules.md` y `product-brief.md` (2026-10-31)
**Responsable:** Rebeca — capa Infrastructure
**Última actualización:** 2026-11-02

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
| **U-03** | Qué hace `UpdateStatusAsync` si el `Id` no existe (BR-P-02). | Feli | T1.7 |
| **U-04** | Orden de las listas (BR-P-11), incluido el de `GetAllAsync` de eventos, que no tiene definición. | Feli | T1.9 |
| **U-05** | **Resuelto:** el mensaje incluye `AmountCents` y `Currency`, no agrega `correlationId` al cuerpo y `RequestedAt` es `DateTimeOffset` (BR-P-06, BR-P-07). | Gian | T3.5, T3.6 |
| **U-06** | **Resuelto (2026-11-02):** se verificó desde una aplicación de consola externa que la suscripción `booking` existe en el topic `grupo5-booking-topic`. | Gian / profesor | T3.1, T3.9 |
| **U-07** | Forma del JSON de la consulta de estado y qué devuelve si Booking aún no procesó la solicitud (BR-P-08, BR-P-09). | Santi | T4.7 |
| **U-08** | Header `X-Correlation-Id` en las llamadas a Booking (BR-P-10). | Santi | T4.8 |
| **U-09** | PostgreSQL en el compose: servicio, versión, host y credenciales para la connection string; `depends_on` con healthcheck. | Felipe | T1.11, T2.4, T2.5 |
| **U-10** | Dirección de Booking en el compose y nombre de la variable. | Felipe / Santi | T4.5, T4.10 |
| **U-11** | Nombre de la variable de entorno del secreto del client `availability` en su contenedor. | Felipe | T4.4, T4.10 |
| **U-12** | Token endpoint desde la red del compose y efecto sobre el emisor (`iss`), porque el compose fija `KC_HOSTNAME` en `http://localhost:8081`. | Felipe | T4.3, T4.10 |
| **U-13** | Convención de logs más allá del `correlationId`: formato, proveedor y campos. | Felipe | T5.5 |
| **U-14** | Qué correlación usar en consultas sin `requestId` (listado de asientos y ocupación), o si se dejan fuera de la auditoría de cadenas. | Felipe / Feli / Luciana | T5.6 |
| **U-15** | Proyecto Api con el paquete `Microsoft.EntityFrameworkCore.Design` y un `Program.cs` que llame a `AddInfrastructure` y `ApplyMigrationsAsync`. | Luciana | T1.10, T2.3, T2.5 |
| **U-16** | Framework de pruebas y estrategia para los repositorios (PostgreSQL real o base en memoria). | Grupo | T6.1 y todas las pruebas de F6 |
| **U-17** | Valores de timeouts y reintentos del cliente HTTP y del publisher. | Grupo | T5.1, T5.3, T5.4 |
| **U-18** | Si el publisher usa los reintentos del SDK de Service Bus o Polly. | Grupo | T5.2, T5.4 |
| **U-19** | Ubicación y formato del historial de prompts (el reparto indica `/prompts` con `Nombre_Apellido.md` y la guía `prompts/Nombre_Apellido/`) y de la carpeta de decisiones. | Grupo / Felipe | T7.2, T7.3 |
| **U-20** | Quién del subgrupo arma el diagrama de Availability, la marca de consistencia eventual y la tabla de interpretación. | Subgrupo | T7.4 |
| **U-21** | Verificar que `feat/availability` existe y que Domain y Application tienen las entidades e interfaces acordadas con Feli. | Rebeca / Feli | T0.1 |

### Qué conviene cerrar primero

Ordenados por cuántas tareas destraban:

1. **U-09 a U-13 (Felipe):** se pueden preguntar en una sola conversación y destraban PostgreSQL, Keycloak y Booking en el compose.
2. **U-15 (Luciana):** destraba la migración inicial y el arranque.
3. **U-03 y U-04 (Feli):** definen el caso de solicitud inexistente y el orden de las listas. U-02, U-05 y U-06 ya están resueltos.
4. **U-07 y U-08 (Santi).**
5. **U-16, U-17 y U-18 (Grupo)** pueden esperar a las fases finales.

---

## 4. Fase 0 — Preparación del proyecto

**Objetivo:** tener la estructura de Infrastructure lista y compilando.
**Depende de:** U-21.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T0.1** | Verificar que `feat/availability` existe y que Domain y Application tienen las entidades (`ReservationRequest`, `Event`, `Seat`, `RequestStatus`) y las cinco interfaces acordadas con Feli. | U-21 | Lista (verificación) | — |
| **T0.2** | Crear `feat/availability-rebeca` desde `feat/availability`. | T0.1 | Lista (tras T0.1) | — |
| **T0.3** | Crear la estructura de carpetas de la spec §3 y las referencias a Application y Domain. | T0.2 | Lista (tras T0.1) | — |
| **T0.4** | Agregar los paquetes de la spec §3 (provider de Npgsql, `Azure.Messaging.ServiceBus`, `Microsoft.Extensions.Http.Resilience`), fijando la última versión estable compatible con .NET 10. | T0.3 | Lista (tras T0.1) | — |
| **T0.5** | Crear las clases de opciones `ServiceBusOptions`, `BookingClientOptions` y `KeycloakOptions`. | T0.3 | Lista (tras T0.1) | — |
| **T0.6** | Armar la lista de claves de configuración de Availability (spec §4) para pasársela a Felipe, que mantiene el compose. Los valores de Booking y del secreto de Keycloak se completan cuando se resuelvan U-10 y U-11. | T0.5 | Lista | — |

**Se considera terminada cuando:** la solución compila y la estructura coincide con la spec §3.
**PR 0:** `feat(availability): estructura de la capa Infrastructure`.

---

## 5. Fase 1 — Persistencia

**Objetivo:** modelo de datos, repositorios y migración inicial.
**Depende de:** F0.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T1.1** | Definir los datos del seed: cuántos eventos y asientos, con GUID fijos, `StartsAt` en UTC, `PriceCents` y `Currency`. Después compartirlos con quien arme las pruebas de punta a punta. | F0 | Lista | — |
| **T1.2** | `AvailabilityDbContext` con `ApplyConfigurationsFromAssembly`. | F0 | Lista | — |
| **T1.3** | Configuraciones de `ReservationRequest`, `Event` y `Seat`: claves, `ValueGeneratedNever`, conversión de `Status` a texto, índices por `UserId` y `EventId`, clave foránea, y persistencia de `Event.PriceCents` y `Event.Currency`. | T1.2 | Lista | — |
| **T1.4** | Seed con `HasData`. | T1.1, T1.3 | Lista (tras T1.1) | — |
| **T1.5** | `ReservationRequestRepository`: `SaveAsync`, `GetByIdAsync` (devuelve `null` si no existe) y `GetByUserIdAsync`, sin orden definido. | T1.3 | Lista | — |
| **T1.6** | `UpdateStatusAsync`: cambia únicamente el estado de una solicitud existente. | T1.5 | Lista | — |
| **T1.7** | `UpdateStatusAsync`: comportamiento si la solicitud no existe. | T1.6, U-03 | **Bloqueada (U-03)** | Lanzar `KeyNotFoundException` (spec §5.3) |
| **T1.8** | `EventRepository` (`GetAllAsync`, `GetByIdAsync`) y `SeatRepository` (`GetByEventAsync`, `ExistsAsync` sobre el par evento + asiento), de solo lectura con `AsNoTracking`, sin orden definido. | T1.3 | Lista | — |
| **T1.9** | Aplicar el orden de las listas. | T1.5, T1.8, U-04 | **Bloqueada (U-04)** | Solicitudes de más reciente a más antigua; asientos por `Label`; eventos por `StartsAt` (este último es una propuesta mía: no está definido) |
| **T1.10** | Generar la migración inicial `InitialCreate` con `dotnet ef`, usando el Api como proyecto de inicio. | T1.4, T1.8, U-15 | **Bloqueada (U-15)** | `IDesignTimeDbContextFactory` en Infrastructure para generarla sin depender del Api |
| **T1.11** | Verificar contra un PostgreSQL real que se crean las tres tablas y se carga el seed. | T1.10, U-09 | **Bloqueada (U-09)** | Un PostgreSQL local temporal, solo si lo autorizás |
| **T1.12** | Comentar en el código las decisiones y patrones (Repository, Fluent API, `AsNoTracking`, `ValueGeneratedNever`, UTC). | T1.5, T1.8 | Lista | — |

**Se considera terminada cuando:** el código compila y los repositorios cumplen BR-01, BR-03, BR-06 a BR-14. La migración y su verificación (T1.10 y T1.11) cierran los criterios de aceptación de tablas y seed.
**PR 1:** `feat(availability): persistencia con PostgreSQL`.

---

## 6. Fase 2 — Arranque y registro de dependencias

**Objetivo:** que la app migre la base al iniciar y que la capa se registre con una sola llamada.
**Depende de:** F1.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T2.1** | `DatabaseInitializer.ApplyMigrationsAsync` (equivalente asíncrono de `Database.Migrate()`). | F1 | Lista (tras F1) | — |
| **T2.2** | `AddInfrastructure(IServiceCollection, IConfiguration)`: opciones, `DbContext` con `ConnectionStrings:AvailabilityDb`, repositorios y `TimeProvider`. Los registros del publisher, el token y el cliente de Booking se suman en F3 y F4. | F1 | Lista (tras F1) | — |
| **T2.3** | Coordinar con Luciana que `Program.cs` llame a `AddInfrastructure(builder.Configuration)` y a `ApplyMigrationsAsync()`. | T2.1, T2.2, U-15 | **Bloqueada (U-15)** | — |
| **T2.4** | Que Availability espere a que PostgreSQL esté listo: `depends_on` con healthcheck en el compose y reintentos de conexión en Npgsql. | U-09 | **Bloqueada (U-09)** | `EnableRetryOnFailure()` en las opciones de Npgsql (propuesta técnica de la spec §5.4) |
| **T2.5** | Verificar que la app migra y siembra la base al iniciar con `docker-compose up`. | T2.3, T2.4, U-09, U-15 | **Bloqueada (U-09, U-15)** | — |

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
| **T3.3** | Registrar `ServiceBusClient` y `ServiceBusSender` como singleton, leyendo `ServiceBus:ConnectionString` y `ServiceBus:TopicName`, sin opciones de reintento. | F0 | Lista | — |
| **T3.4** | `ServiceBusReservationPublisher`: mapear la solicitud y el evento (`AmountCents` = `Event.PriceCents`, `Currency` = `Event.Currency`), serializar en JSON camelCase con `ContentType: application/json`, usar `RequestId` para `MessageId` y `CorrelationId`, y lanzar `ExternalServiceUnavailableException` si falla el envío. | T3.2, T3.3 | **Implementada** | — |
| **T3.5** | Incluir en el cuerpo `AmountCents` y `Currency`; no agregar `correlationId` al JSON. | T3.4, U-05 | **Resuelta por contrato** | — |
| **T3.6** | Confirmar que el contrato define `RequestedAt` como `DateTimeOffset`. | U-05 | **Confirmada por contrato** | — |
| **T3.7** | Subir el contrato del mensaje a `/contracts/events/` con una rama `chore/contracts-*` hacia `develop`, con aprobación de los dos subgrupos. **PR aparte.** Verificar primero si ya existe. | Contrato cerrado con Gian | Lista | — |
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
| **T4.1** | `KeycloakTokenProvider`: `client_credentials`, caché del token y renovación 30 segundos antes del vencimiento, con un semáforo para no pedir varios tokens a la vez. | F0 | Lista | — |
| **T4.2** | `BookingAuthHandler`: agrega `Authorization: Bearer` en cada llamada. | T4.1 | Lista (tras T4.1) | — |
| **T4.3** | Definir el token endpoint que se usa dentro de la red del compose. | U-12 | **Bloqueada (U-12)** | `http://keycloak:8081/realms/seat-reservation/protocol/openid-connect/token` (propuesta de la spec §4, sin verificar) |
| **T4.4** | Fijar el nombre de la variable del secreto del client `availability` en el contenedor de Availability. | U-11 | **Bloqueada (U-11)** | — |
| **T4.5** | Fijar `Booking:BaseUrl`. | U-10 | **Bloqueada (U-10)** | — |
| **T4.6** | `BookingClient.GetOccupiedSeatIdsAsync`: `GET api/events/{eventId}/occupied-seats`, deserializa `[{ seatId, isOccupied }]` y devuelve los `seatId` con `isOccupied` en `true`; lista vacía con `200` significa "nada ocupado". Si Booking falla, lanza `ExternalServiceUnavailableException`. | T4.2 | **Lista (tras T4.2)** | — |
| **T4.7** | `BookingClient.GetStatusByRequestIdAsync`: `GET api/reservations/by-request/{requestId}`, `null` ante `404`, `ExternalServiceUnavailableException` ante otros errores. | T4.6, U-07 | **Bloqueada (U-07)** | Deserializar a `BookingReservationStatusDto(Status, Description)` con nombres camelCase (spec §7.2, sin confirmar) |
| **T4.8** | Enviar el header `X-Correlation-Id` con el `requestId`. | T4.7, U-08 | **Bloqueada (U-08)** | — |
| **T4.9** | Registrar en `AddInfrastructure` el `KeycloakTokenProvider` (singleton), el handler y el `HttpClient` tipado de `BookingClient`, sin resiliencia todavía. | T4.2, T4.6 | Lista (tras T4.6) | — |
| **T4.10** | Verificar contra Keycloak y Booking reales: el token se pide una sola vez mientras es válido y las dos consultas responden. | T4.3, T4.4, T4.5, T4.7, U-10, U-11, U-12 | **Bloqueada (U-10, U-11, U-12)** | — |
| **T4.11** | Comentar las decisiones: caché del token, handler delegante, `404` como `null`, errores que se distinguen. | T4.7 | Lista (tras T4.7) | — |

**Se considera terminada cuando:** el cliente obtiene un token del client `availability`, lo cachea y consulta los dos endpoints de Booking (BR-22 a BR-30).
**PR 4:** `feat(availability): cliente HTTP a Booking con token`.

---

## 9. Fase 5 — Reintentos, timeouts y auditoría

**Objetivo:** que el cliente HTTP y el publisher toleren fallas, y que cada operación deje trazas de auditoría.
**Depende de:** F3 y F4 (y de F1 para los logs del repositorio).

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T5.1** | Definir los valores de timeouts y reintentos del cliente HTTP y del publisher. | U-17 | **Bloqueada (U-17)** | Valores por defecto de `AddStandardResilienceHandler` y del SDK de Service Bus |
| **T5.2** | Confirmar si el publisher usa los reintentos del SDK o Polly. | U-18 | **Bloqueada (U-18)** | `ServiceBusRetryOptions` del SDK, sin Polly, para no multiplicar los reintentos (spec §8.2) |
| **T5.3** | Agregar `AddStandardResilienceHandler()` al `BookingClient` y al `HttpClient` de Keycloak, **registrado antes** del handler de token para que cada reintento use el token vigente. | F4, T5.1 | **Bloqueada (U-17)** | Valores por defecto |
| **T5.4** | Configurar `ServiceBusRetryOptions` en el `ServiceBusClient`. | F3, T5.1, T5.2 | **Bloqueada (U-17, U-18)** | Valores por defecto del SDK |
| **T5.5** | Aplicar la convención de logs de Felipe (formato, proveedor, campos). | U-13 | **Bloqueada (U-13)** | Seguir con `ILogger<T>` sin formato propio |
| **T5.6** | Definir la correlación de las consultas sin `requestId` (listado de asientos y ocupación). | U-14 | **Bloqueada (U-14)** | Ninguna: queda sin correlación de cadena hasta que se decida |
| **T5.7** | Logs `Started`, `Completed` y `Failed` con un scope `CorrelationId` igual al `requestId` en el publisher, en la consulta de estado de `BookingClient` y en `ReservationRequestRepository`. Usa `ILogger<T>` (propuesta de la spec §9). Nunca se registran secretos, tokens ni la connection string. | F1, F3, F4 | Lista (tras F1, F3 y F4) | — |

**Se considera terminada cuando:** hay reintentos y timeouts configurados en los dos clientes, y los logs de auditoría permiten seguir un `requestId` por Availability (BR-41).
**Nota:** como los reintentos pueden duplicar un mensaje, la protección es el `MessageId` igual al `requestId` y la idempotencia de Booking por `requestId`.
**PR 5:** `feat(availability): resiliencia y auditoría`.

---

## 10. Fase 6 — Pruebas

**Objetivo:** verificar los criterios de aceptación del product brief (sección 12).
**Depende de:** F1 a F5 y de U-16.

| ID | Tarea | Depende de | Estado | Provisorio posible |
|---|---|---|---|---|
| **T6.1** | Decidir con el grupo el framework de pruebas y si los repositorios se prueban contra PostgreSQL real o en memoria. Una base en memoria no reproduce algunas restricciones de PostgreSQL. | U-16 | **Bloqueada (U-16)** | — |

Pruebas a escribir una vez resuelta T6.1:

| ID | Prueba | Regla | Depende de | Estado |
|---|---|---|---|---|
| **T6.2** | `SaveAsync` con `Id` repetido falla | BR-08 | T6.1, F1 | **Bloqueada (U-16)** |
| **T6.3** | `UpdateStatusAsync` cambia el estado; los tres valores del enum se guardan y se leen | BR-10, BR-11 | T6.1, F1 | **Bloqueada (U-16)** |
| **T6.4** | `ExistsAsync` es `false` si el asiento no pertenece al evento | BR-01 | T6.1, F1 | **Bloqueada (U-16)** |
| **T6.5** | `GetByUserIdAsync` devuelve solo las solicitudes del usuario | BR-14 | T6.1, F1 | **Bloqueada (U-16)** |
| **T6.6** | El seed carga al menos un evento con asientos y es reproducible | BR-04 | T6.1, F1 | **Bloqueada (U-16)** |
| **T6.7** | El publisher envía el JSON acordado con `MessageId` y `CorrelationId` iguales al `requestId` | BR-17, BR-19 | T6.1, F3 | **Bloqueada (U-16)** |
| **T6.8** | El cliente devuelve los `seatId` con `isOccupied` en `true`, y vacío si Booking responde `200` con lista vacía (con un `HttpMessageHandler` falso) | BR-25, BR-26 | T6.1, F4 | **Bloqueada (U-16)** |
| **T6.9** | `GetStatusByRequestIdAsync` devuelve `null` con `404` y lanza excepción ante un error | BR-27, BR-28 | T6.1, F4 | **Bloqueada (U-16, U-07)** |
| **T6.10** | El token se pide una sola vez mientras es válido y se envía en cada llamada | BR-29, BR-30 | T6.1, F4 | **Bloqueada (U-16)** |

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

| Fase | Tareas | Listas | Bloqueadas | Pendientes de autorización |
|---|---|---|---|---|
| F0 | 6 | 6 | 0 | 0 |
| F1 | 12 | 8 | 4 | 0 |
| F2 | 5 | 2 | 3 | 0 |
| F3 | 10 | 9 | 0 | 1 |
| F4 | 11 | 4 | 7 | 0 |
| F5 | 7 | 1 | 6 | 0 |
| F6 | 10 | 0 | 10 | 0 |
| F7 | 6 | 3 | 3 | 0 |

**Por dónde arrancar ya mismo, sin esperar a nadie:** T0.1 a T0.6, y después T1.1 a T1.6, T1.8 y T1.12 en paralelo con T3.3, T3.7, T4.1 y T4.2.
