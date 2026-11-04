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
| `Booking.Business.Tests` | Tests | 24 casos de la lógica de reservas (más los de las consultas de Santi) | Gian |
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

Ya está registrado: `AddReservationRequestConsumer(builder.Configuration)`, después de
`AddBookingData` (repositorio de Luis) y `AddPaymentCommunication` (cliente de Santi).
Sin `Broker:ConnectionString` la API no arranca, salvo en el entorno `Testing`.

Para correr `Booking.Api` localmente:

```powershell
dotnet user-secrets set "ConnectionStrings:Booking" "<connection string de Postgres>" --project src/Booking/Booking.Api
dotnet user-secrets set "Broker:ConnectionString" "<connection string del profe>" --project src/Booking/Booking.Api
dotnet user-secrets set "Payment:ClientSecret" "booking-secret" --project src/Booking/Booking.Api
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

## Estado de la integración

| Pieza | Estado |
|---|---|
| Contrato del mensaje | ✅ `contracts/events/ReservationRequestedMessage.cs` (Rebeca). Booking compila ese archivo, no una copia. |
| Importe y moneda | ✅ Se copian del mensaje a la reserva y se cobran con `ReservationPaymentClient` (Santi). |
| Repositorio Postgres | ✅ `ReservationRepository` (Luis), con migración de importe y moneda. |
| Cliente gRPC a Payment | ✅ `GrpcPaymentGateway` + `ReservationPaymentClient` (Santi). |
| Publisher de Availability | ⏳ Rebeca: completar `AmountCents` y `Currency` con el precio del evento. |
| Servidor de Payment | ⏳ Agus y Juan: simulador de pagos idempotente por `payment_operation_id`. |
| Notifications | ⏳ Juan y Kevin: hoy los eventos solo se loguean (`IReservationEventPublisher`). |
| Docker | ⏳ Dockerfile de Booking y bloque en el compose con 2 réplicas. |

## Decisiones (resumen para la defensa)

- **Concurrencia:** la decide la base con un índice único parcial
  `(event_id, seat_id) WHERE status IN ('Held', 'Confirmed')`, no un `if` en C#.
  Así funciona con varias réplicas.
- **Idempotencia:** Service Bus entrega "al menos una vez". Un índice único por
  `request_id` evita reservas duplicadas. Si la reserva quedó Held por un corte, el
  reintento retoma el cobro, y Payment no cobra dos veces porque es idempotente.
  Si dos réplicas reciben la misma solicitud a la vez, solo cobra la que insertó la
  reserva; la otra no llama a Payment.
- **Transiciones condicionales:** el resultado del pago y el job de vencimiento usan
  `UPDATE` con condición sobre el estado, así no se pisan.
- **Respuesta de Payment vs. error de comunicación:** solo una respuesta explícita
  de Payment rechaza (`PAYMENT_REJECTED` → `PaymentFailed`, `PAYMENT_UNAVAILABLE` →
  `PaymentUnavailable`). Cualquier error gRPC, incluido `StatusCode.Unavailable` y el
  timeout, es `Uncertain`: Payment pudo haber cobrado, así que la reserva queda Held,
  se reintenta y, si nunca hay resultado, vence (`Expired` = "sin resultado de pago").
  Ese es el flujo de fallo de 3j cuando Payment está caído.
- **Confirmar dentro del plazo:** el resultado del pago se aplica con
  `UPDATE ... WHERE status = 'Held' AND expires_at > @now`, atómico en la base. Si el
  plazo pasó, no se confirma aunque el job de vencimiento todavía no haya corrido.
- **Pago exitoso después de vencer:** no se confirma (el asiento pudo pasar a otro);
  queda en el log como caso para devolución (pendiente: guardarlo en la tabla como
  `REFUND_REQUIRED`).
- **Competing consumers:** todas las réplicas leen la suscripción `booking` y cada
  mensaje lo procesa una sola.
- **Completar después de guardar:** `AutoCompleteMessages = false`. Si hay un error,
  el mensaje se abandona y se reintenta; un JSON inválido va directo a la dead-letter queue.
