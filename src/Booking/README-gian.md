# Booking: consumidor del broker y lógica de reservas (Gian)

Parte de Booking que toma las solicitudes del broker, crea la reserva retenida o la
rechaza, cobra por gRPC, confirma o rechaza según el pago, y vence las retenidas
que no tuvieron respuesta (pasos 4, 5, 6 y 8 del flujo).

Usa los nombres del documento "Catálogos y tablas por servicio": tabla `reservations`,
estados `Held/Confirmed/Rejected/Expired` guardados como texto, ids `Guid`.

## Dónde está cada cosa

| Proyecto / carpeta | Capa | Qué tiene | Dueño |
|---|---|---|---|
| `Booking.Data/Entities` | Datos | `Reservation`, `ReservationStatus`, `RejectionReason` (anémicas) | Gian, revisar con Luis |
| `Booking.Data` | Datos | Interfaz `IReservationRepository` con el SQL esperado en comentarios | Interfaz: Gian. Implementación EF: **Luis** |
| `Booking.Business` | Negocio | `ReservationRequestProcessor`, `ReservationExpirationService`, `ReservationExpirationWorker`, `IPaymentClient`, `IReservationEventPublisher` | Gian |
| `Booking.Api/Messaging` | Presentación (entrada por broker) | `ReservationRequestedConsumer` con `ServiceBusProcessor` | Gian |
| `Booking.Business.Tests` | Tests | 18 casos con fakes en memoria | Gian |
| `Booking.Demo` | Herramienta | Simulador local (no se despliega) | Gian |

Dependencias N-Layer: `Api → Business → Data`. Data no conoce a nadie.

## Simulador (`Booking.Demo`)

Corre la lógica real de Booking sin Azure, Postgres ni Payment. Reemplaza el broker por
uno en memoria con 3 réplicas y reintentos, la base por un repositorio en memoria con las
mismas restricciones, Payment por un simulador idempotente, y Notifications por la consola.

```bash
dotnet run --project src/Booking/Booking.Demo            # menú interactivo
dotnet run --project src/Booking/Booking.Demo -- --auto  # corre los 8 escenarios
```

Con Azure Service Bus real (los mensajes van al topic y los consume el
`ReservationRequestedConsumer` de Booking.Api; base y Payment siguen simulados):

```powershell
$env:Broker__ConnectionString = "<connection string del profe>"   # solo en esa ventana
dotnet run --project src/Booking/Booking.Demo -- --azure
```

Avisar al grupo antes: consume de la suscripción compartida `booking`.

Probado contra el Azure Service Bus de la cátedra (`grupo5-booking-topic` / `booking`):
happy path, carrera de 10 solicitudes y reintento por timeout (abandon + reentrega).

Escenarios: happy path, asiento ocupado, pago rechazado, Payment "no disponible",
carrera de 10 personas por el mismo asiento, mensaje duplicado, timeout con cobro
y Payment que nunca responde (la reserva vence). Retención de 15 s para ver el vencimiento.

## Cómo se engancha en `Booking.Api/Program.cs`

Todavía NO está registrado en `Program.cs` a propósito: sin el repositorio de Luis y el
cliente gRPC de Santi, la app no arrancaría. Cuando estén, se agregan estas tres líneas
antes de `builder.Build()`:

```csharp
builder.Services.AddScoped<IReservationRepository, ReservationRepository>(); // Luis
builder.Services.AddScoped<IPaymentClient, PaymentGrpcClient>();             // Santi
builder.Services.AddReservationRequestConsumer(builder.Configuration);       // Gian (using Booking.Api.Messaging)
```

## Configuración

```json
"Reservations": { "HoldDuration": "00:02:00", "ExpirationInterval": "00:00:30" },
"Broker":       { "Topic": "grupo5-booking-topic", "Subscription": "booking", "MaxConcurrentCalls": 4 }
```

`Topic` tiene que coincidir con el `EntityPath` de la connection string.

La connection string va por variable de entorno o user-secrets, nunca en el repo
(el repo de entrega es público):

```bash
dotnet user-secrets set "Broker:ConnectionString" "<connection string del profe>" --project src/Booking/Booking.Api
# o en docker-compose:  Broker__ConnectionString: ${BOOKING_SERVICEBUS_CONNECTION}
```

## Qué falta acordar

| Con | Qué | Dónde está |
|---|---|---|
| **Rebeca y Felipe** | Contrato del mensaje (`requestId`, `eventId`, `seatId`, `userId`, `requestedAt`, JSON camelCase). Publicar un record propio, **no la entidad `ReservationRequest`** (llegaría `id` en vez de `requestId` y todo iría a dead-letter). Pedirle `MessageId = requestId`. Sumar `amountCents` y `currency` cuando `Event` tenga precio. | `Booking.Business/Messages/ReservationRequestedMessage.cs`, después a `/contracts` |
| **Luis** | Implementar `IReservationRepository` con los tres índices, los estados como texto y los `UPDATE` condicionales. Sumar `GetOccupiedSeatIdsAsync(eventId)` para el endpoint de asientos ocupados. | `Booking.Data/IReservationRepository.cs` |
| **Santi** | Implementar `IPaymentClient` (con token de Keycloak, `aud=payment`). Endpoints que pide `IBookingClient` de Availability: `GET /events/{eventId}/occupied-seats` y `GET /reservations/{requestId}` → `{status, description}`, 404 si todavía no se procesó. | `Booking.Business/Payments/IPaymentClient.cs` |
| **Grupo** | Mapeo de errores de Payment: respuesta explícita `PAYMENT_UNAVAILABLE` → `Rejected (PaymentUnavailable)`. Error gRPC `UNAVAILABLE` (Payment caído): hoy → rechazo; propuesta → `Uncertain` (reintenta y, si no hay resultado, vence). Timeout → `Uncertain`. `PAYMENT_PENDING` no hace falta; si se agrega, → `Uncertain`. | `IPaymentClient.cs` |
| **Agus y Juan** | Payment idempotente por `ReservationId` (`payments.reservation_id` UNIQUE): si se reintenta, devuelve el resultado anterior sin volver a cobrar. | Contrato `.proto` |
| **Juan y Kevin** | Cómo le llegan a Notifications `ReservationCreated` y `ReservationConfirmed`: suscripción nueva pedida al profe, o HTTP. Por ahora solo se loguean. | `Booking.Business/Events/IReservationEventPublisher.cs` |
| **Felipe** | Formato de los logs de auditoría (el correlation id ya es `RequestId`). | `Booking.Business/Auditing/AuditLog.cs` |

## Decisiones (resumen para la defensa)

- **Concurrencia:** la decide la base con un índice único parcial
  `(event_id, seat_id) WHERE status IN ('Held', 'Confirmed')`, no un `if` en C#.
  Así funciona con varias réplicas.
- **Idempotencia:** Service Bus entrega "al menos una vez". Un índice único por
  `request_id` evita reservas duplicadas. Si la reserva quedó Held por un corte, el
  reintento retoma el cobro, y Payment no cobra dos veces porque es idempotente.
  Si dos réplicas reciben la misma solicitud a la vez, solo cobra la que insertó la
  reserva; la otra no llama a Payment.
- **Transiciones condicionales:** `UPDATE ... WHERE status = 'Held'`. El resultado del
  pago y el job de vencimiento no se pisan.
- **Payment caído vs. timeout:** si Payment no está (`Unavailable`), seguro no cobró:
  se rechaza con `PaymentUnavailable` y se libera el asiento (flujo de fallo de 3j).
  Si hay timeout (`Uncertain`), Payment pudo haber cobrado: la reserva queda Held, el
  mensaje se reintenta y, si nunca llega un resultado, vence (`Expired` = "sin
  resultado de pago", como dice la consigna).
- **Pago exitoso después de vencer:** no se confirma (el asiento pudo pasar a otro);
  queda en el log como caso para devolución (pendiente: guardarlo en la tabla como
  `REFUND_REQUIRED`).
- **Competing consumers:** todas las réplicas leen la suscripción `booking` y cada
  mensaje lo procesa una sola.
- **Completar después de guardar:** `AutoCompleteMessages = false`. Si hay un error,
  el mensaje se abandona y se reintenta; un JSON inválido va directo a la dead-letter queue.
