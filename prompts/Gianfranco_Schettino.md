# Historial de prompts — Gianfranco Schettino

Parte asignada: consumidor del broker y lógica de negocio de Booking (reserva retenida,
rechazada, idempotencia por id de solicitud y job de vencimiento, paso 8 del flujo).

## 1. Planificación antes de escribir código

- **Fecha:** 30/10/2026
- **Herramienta:** Claude Code (Claude Opus 5.5)
- **Rama:** `feat/booking-gian`

### Prompt

> Soy Gian, del subgrupo Booking (N-Layer: Api → Business → Data, .NET 10). Me toca el consumidor del broker y la lógica de negocio: cuando llega una solicitud de reserva desde Availability hay que crear la reserva retenida o rechazarla si el asiento está ocupado, cobrar a Payment por gRPC y confirmar o rechazar según el pago. Además el job que vence las retenidas que no tuvieron respuesta (paso 8 del diagrama).
>
> El broker es Azure Service Bus (topic y suscripción `booking`, la connection string la pasa el profe). Luis hace la capa de datos con Postgres y Santi el cliente gRPC a Payment, así que no implementes eso: definí las interfaces que necesito de ellos y dejá comentado qué se espera de cada método.
>
> Antes de tocar archivos mostrame qué proyectos y clases proponés crear, en qué capa va cada una y cómo resolvés la concurrencia (dos personas por el mismo asiento) y la idempotencia (Service Bus entrega al menos una vez). Esperá mi OK.

### Correcciones

- La primera propuesta controlaba el asiento ocupado con un `SELECT` y después un `INSERT` en C#. Con varias réplicas eso tiene una carrera: le pedí que la decisión la tome la base con un índice único parcial `(event_id, seat_id) WHERE status IN ('Held','Confirmed')` y que el repositorio devuelva `SeatOccupied` en vez de lanzar.
- Le pedí usar los nombres del documento "Catálogos y tablas por servicio" (tabla `reservations`, estados `Held/Confirmed/Rejected/Expired` como texto, ids `Guid`) para no tener que renombrar después con Luis.

## 2. Consumidor, procesador de reservas y vencimiento

- **Fecha:** 30/10/2026
- **Herramienta:** Claude Code (Claude Opus 5.5)
- **Rama:** `feat/booking-gian`

### Prompt

> Dale, implementá el plan:
>
> - `Booking.Data`: entidades anémicas `Reservation`, `ReservationStatus`, `RejectionReason` y la interfaz `IReservationRepository` (sin implementación, esa es de Luis). Con `TryHoldAsync` que devuelva Created / SeatOccupied / DuplicateRequest, `GetByRequestIdAsync`, `TryResolveHeldAsync` y `ExpireHeldAsync`. Poné en comentarios el SQL que se espera de cada uno.
> - `Booking.Business`: `ReservationRequestProcessor` con la lógica (retener, rechazar, cobrar, confirmar/rechazar), `IPaymentClient` para lo de Santi, `IReservationEventPublisher` para Notifications, y `ReservationExpirationService` + un `BackgroundService` que corra cada X segundos.
> - `Booking.Api/Messaging`: `ReservationRequestedConsumer` con `ServiceBusProcessor`.
>
> Reglas: si llega un mensaje con un `requestId` que ya tiene reserva no se crea otra; si quedó Held por un corte, el reintento retoma el cobro. Los cambios de estado tienen que ser `UPDATE` condicionales sobre `status = 'Held'` así el pago y el job de vencimiento no se pisan. Completá el mensaje solo después de guardar (`AutoCompleteMessages = false`); si falla, abandon; si el JSON es inválido, dead-letter.
>
> Logs de auditoría con el `requestId` como correlation id, como acordamos. Duración de la retención y el intervalo del job en `appsettings`. Comentá en español las decisiones, que nos lo piden en el punto 3. Al final agregá un README corto de mi parte en `src/Booking`.

### Correcciones

- Si dos réplicas reciben la misma solicitud a la vez, las dos llamaban a Payment. Le pedí que solo cobre la que efectivamente insertó la reserva; la que recibe `DuplicateRequest` no cobra.
- Pago exitoso después de vencida: no se confirma porque el asiento pudo pasar a otro. Queda en el log como caso de devolución (pendiente guardarlo como `REFUND_REQUIRED`).

## 3. Tests de la lógica de reservas

- **Fecha:** 30/10/2026
- **Herramienta:** Claude Code (Claude Opus 5.5)
- **Rama:** `feat/booking-gian`

### Prompt

> Armá `Booking.Business.Tests` con xUnit para `ReservationRequestProcessor`. Como el repositorio real todavía no existe, hacé un `InMemoryReservationRepository` que respete las mismas restricciones que la base (asiento único entre Held y Confirmed, `requestId` único, update condicional) y fakes para Payment, el publisher y el reloj.
>
> Casos: happy path, asiento ocupado, pago rechazado, Payment no disponible, mensaje duplicado (con la reserva ya confirmada y con la reserva todavía Held), carrera de 10 solicitudes por el mismo asiento (solo una queda Held) y vencimiento de las retenidas. Nombres de los tests en español. Sin librerías de mocking.

## 4. Simulador local

- **Fecha:** 30/10/2026
- **Herramienta:** Claude Code (Claude Opus 5.5)
- **Rama:** `feat/booking-gian`

### Prompt

> Quiero poder mostrar el flujo de Booking sin depender de Postgres, Payment ni Azure, porque los otros todavía no terminaron. Hacé una consola `Booking.Demo` (no se despliega) que use la lógica real de Business pero con: un broker en memoria con 3 réplicas consumiendo y reintentos, el repositorio en memoria, un Payment simulado idempotente y Notifications por consola.
>
> Que tenga un menú para elegir escenarios y un `--auto` que los corra todos: happy path, asiento ocupado, pago rechazado, Payment no disponible, carrera de 10 personas por el mismo asiento, mensaje duplicado, timeout con cobro y Payment que nunca responde (la reserva vence). Retención de 15 s así se ve el vencimiento.
>
> Sumá un modo `--azure` que mande los mensajes al topic real y los consuma el `ReservationRequestedConsumer`, con la connection string por variable de entorno. Nada de secretos en el repo, que es público.

### Correcciones

- La salida de la consola mezclaba los logs de las 3 réplicas y no se entendía. Le pedí un formatter más compacto que muestre la réplica y el `requestId` en cada línea.

## 5. Error de comunicación con Payment como resultado incierto

- **Fecha:** 30/10/2026
- **Herramienta:** Claude Code (Claude Opus 5.5)
- **Rama:** `feat/booking-gian`

### Prompt

> Revisando el procesador me di cuenta de que un `StatusCode.Unavailable` de gRPC o un timeout rechazaban la reserva como `PaymentUnavailable`. Eso está mal: si se cortó la comunicación, Payment pudo haber cobrado igual.
>
> Cambialo así: solo una respuesta explícita de Payment rechaza (`PAYMENT_REJECTED` → `PaymentFailed`, `PAYMENT_UNAVAILABLE` → `PaymentUnavailable`). Cualquier error de comunicación es `Uncertain`: la reserva queda Held, el mensaje se abandona para reintentar, y si nunca hay respuesta la vence el job. Ese es nuestro flujo de fallo de 3j cuando Payment está caído.
>
> Además, el resultado del pago tiene que aplicarse solo si la reserva sigue vigente: `UPDATE ... WHERE status = 'Held' AND expires_at > @now`, atómico en la base, no un chequeo de fecha en C#. Actualizá la interfaz del repositorio, los fakes, el simulador, el README y agregá tests para los dos casos.

## 6. Integración con el contrato compartido y las partes de Luis y Santi

- **Fecha:** 03/11/2026
- **Herramienta:** Claude Code (Claude Opus 5.5)
- **Rama:** `feat/booking-gian`

### Prompt

> Ya están mergeados en `feat/booking` el repositorio de Luis, el cliente gRPC de Santi (`ReservationPaymentClient`) y el contrato del mensaje que acordé con Rebeca en `contracts/events/ReservationRequestedMessage.cs`.
>
> Borrá mi copia del mensaje en Business y compilá el archivo del contrato directamente, así no se desincronizan. El mensaje ahora trae `AmountCents` y `Currency`: copialos a la reserva al retenerla para que el cobro use esos valores. Registrá el consumidor en `Program.cs` después de `AddBookingData` y `AddPaymentCommunication`, y que la API no arranque sin `Broker:ConnectionString` salvo en el entorno `Testing`. Actualizá los tests, el simulador y la tabla de estado de integración del README.

## 7. Validación del mensaje

- **Fecha:** 03/11/2026
- **Herramienta:** Claude Code (Claude Opus 5.5)
- **Rama:** `feat/booking-gian`

### Prompt

> Si en el JSON falta un campo, `System.Text.Json` lo deja en su valor por defecto (`Guid.Empty`, 0, null) y el procesador lo trata como válido. Agregá un `ReservationRequestedMessageValidator` en Business que exija ids no vacíos, `requestedAt`, importe mayor que cero y moneda de 3 letras.
>
> El consumidor manda los inválidos a dead-letter con el motivo, y el procesador vuelve a validar como segunda barrera sin persistir ni cobrar. Tests del validador con un caso por campo.
