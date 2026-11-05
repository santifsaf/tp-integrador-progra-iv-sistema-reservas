# Arquitectura de software — Microservicio Payment

**Trabajo práctico integrador:** Programación IV — UTN
**Componente:** Payment (API gRPC)
**Arquitectura:** N-Layer con dominio anémico
**Responsable del componente gRPC:** Agustín Vasino
**Estado:** **Implementada, validada localmente y fusionada en `feat/payment` mediante el PR #27**
**Referencia:** [PR #27](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/27), commit de merge `a2daa2b`, pruebas automatizadas y verificaciones locales del 04–05/11/2026.
**Actualización del documento:** 05/11/2026, posterior a la aprobación y al merge del PR #27.

## 1. Diagrama de arquitectura

```mermaid
flowchart TB
    BOOKING["Booking.Api<br/>GrpcPaymentGateway"]
    KEYCLOAK["Keycloak<br/>Emite JWT y publica claves públicas (JWKS)"]
    PROTO["Contrato compartido<br/>contracts/proto/payment.proto"]

    subgraph PAYMENT["Microservicio Payment — .NET 10 / N-Layer"]
        direction TB

        subgraph PRESENTACION["Payment.Grpc — Presentación"]
            direction LR
            AUTH["Autenticación y autorización JWT<br/>AuthenticationExtensions / AllowedClientHandler"]
            AUDIT["Auditoría<br/>GrpcAuditInterceptor"]
            GRPC["PaymentGrpcService<br/>Charge / GetPayment"]
            MAPPER["PaymentGrpcMapper<br/>Protobuf ↔ DTOs"]
        end

        subgraph NEGOCIO["Payment.Business — Negocio"]
            direction LR
            I_SERVICE["IPaymentService"]
            SERVICE["PaymentService<br/>Validación e idempotencia"]
            SIMULATOR["PaymentSimulator<br/>Resultado determinista"]
        end

        subgraph DATOS["Payment.Data — Datos"]
            direction LR
            I_REPO["IPaymentRepository"]
            REPO["PaymentRepository"]
            CONTEXT["PaymentDbContext<br/>EF Core / Npgsql"]
            ENTITY["PaymentRecord<br/>Entidad anémica"]
        end
    end

    DB[("PostgreSQL<br/>Base exclusiva de Payment")]

    BOOKING -->|"Charge / GetPayment<br/>HTTP/2 + Bearer JWT"| GRPC
    BOOKING -->|"Solicita token (client_credentials)"| KEYCLOAK
    KEYCLOAK -.->|"Claves públicas para verificar la firma"| AUTH
    AUTH -.->|"Protege las llamadas"| GRPC
    AUDIT -.->|"Registra inicio, fin y correlación"| GRPC
    GRPC -->|"Mapea solicitudes y respuestas"| MAPPER
    GRPC -->|"Delega cobro o consulta"| I_SERVICE
    SERVICE -.->|"Implementa"| I_SERVICE
    SERVICE -->|"Simula cobro"| SIMULATOR
    SERVICE -->|"Consulta y actualiza"| I_REPO
    REPO -.->|"Implementa"| I_REPO
    REPO -->|"Accede a EF Core"| CONTEXT
    CONTEXT --> ENTITY
    CONTEXT -->|"Lectura y escritura"| DB
    PROTO -.->|"Define mensajes y RPC"| GRPC
    PROTO -.->|"Genera cliente"| BOOKING
```

**Convención:** las **flechas continuas** representan invocaciones o acceso a datos. Las **flechas discontinuas** señalan relaciones de implementación, protección, configuración o dependencia de contrato. No todas las flechas discontinuas son llamadas de red.

> **Aclaración sobre Keycloak:** Keycloak **emite y firma** los tokens y publica las claves públicas. La **validación** de firma, emisor, audiencia, vencimiento y cliente autorizado la realiza `Payment.Grpc` mediante su configuración JWT de ASP.NET Core. Booking obtiene el token y lo envía como metadato `authorization` en gRPC.

## 2. Responsabilidad de cada capa

| Capa | Componentes principales | Responsabilidad |
|---|---|---|
| **Presentación** — `Payment.Grpc` | `Program.cs`, `PaymentGrpcService`, `PaymentGrpcMapper`, `AuthenticationExtensions`, `AllowedClientHandler`, `GrpcAuditInterceptor` | Exponer `Charge` y `GetPayment`; validar el formato de entrada y el token; adaptar DTOs y respuestas; traducir excepciones a códigos gRPC; registrar auditoría. |
| **Negocio** — `Payment.Business` | `IPaymentService`, `PaymentService`, `PaymentSimulator`, DTOs y excepciones | Validar reglas de negocio, controlar la idempotencia por `OperationId`, simular el resultado y coordinar las operaciones de persistencia. |
| **Datos** — `Payment.Data` | `IPaymentRepository`, `PaymentRepository`, `PaymentDbContext`, `PaymentRecord`, `PaymentStatus` | Consultar, insertar y actualizar pagos mediante EF Core y PostgreSQL. El índice único de `OperationId` impide duplicar ese identificador. |

Las entidades de Payment mantienen datos; la lógica de negocio reside en `PaymentService`, en línea con el requisito de **dominio anémico**.

## 3. Flujo funcional

1. **Booking** solicita un token a **Keycloak** mediante `client_credentials` y llama a Payment por gRPC/HTTP/2 con dicho token.
2. **Payment.Grpc** valida el JWT y autoriza al cliente `booking`. El interceptor registra el inicio, el fin y el identificador de correlación.
3. **`PaymentGrpcService`** valida los parámetros del contrato, normaliza los GUID al formato `D` (minúsculas y guiones), utiliza **`PaymentGrpcMapper`** y delega en **`IPaymentService`**. La normalización también se aplica en `GetPayment` para que variantes de mayúsculas/minúsculas identifiquen la misma operación.
4. **`PaymentService`** busca la operación por `OperationId`. Si es nueva, registra el pago inicialmente como `PENDING`; cuando corresponde, el **`PaymentSimulator`** determina el resultado.
5. **`PaymentRepository`** utiliza **`PaymentDbContext`** para persistir el estado en la base propia de Payment.
6. **`Charge`** devuelve el resultado del cobro simulado. **`GetPayment`** consulta una operación existente sin iniciar otro cobro.

## 4. Resultados y códigos de error

| Situación | Resultado |
|---|---|
| Pago simulado exitoso | `PaymentSucceeded` |
| Pago simulado rechazado | `PaymentRejected` |
| Simulador no disponible | `PaymentUnavailable` |
| Operación registrada sin resultado definitivo | `PaymentPending` |
| Mismo `OperationId` con datos diferentes | gRPC `ALREADY_EXISTS` (`PaymentConflictException`) |
| Datos de negocio inválidos | gRPC `INVALID_ARGUMENT` (`PaymentValidationException`) |
| `GetPayment` sobre una operación inexistente | gRPC `NOT_FOUND` |

**Alcance del simulador actual:** montos terminados en `99` producen rechazo; terminados en `33`, indisponibilidad; los demás, éxito. Esto es una regla de simulación para el TP, **no una pasarela de pagos real**.

## 5. Evidencia de validación local (05/11/2026)

### 5.1. Pruebas automatizadas

En `feat/payment-agus`, tras incorporar las correcciones de gRPC y las migraciones de EF Core del PR #29, se ejecutó:

```powershell
dotnet test src/Payment/Payment.Tests/Payment.Tests.csproj
```

**Resultado observado:** **77 pruebas aprobadas, 0 errores, 0 omitidas**. Se ejecutaron las pruebas con Testcontainers/PostgreSQL, incluyendo las pruebas del repositorio que aplican migraciones con `MigrateAsync()`. Las pruebas de gRPC verifican la normalización de GUID, además de validaciones, mapeo y códigos de error.

### 5.2. Migraciones y persistencia reales

- Se creó una base de pruebas independiente, `payment_migrations_test`, para no alterar la base usada previamente con `EnsureCreatedAsync()`.
- Se aplicó la migración `20261104232835_InitialCreate` mediante `dotnet ef database update --project src/Payment/Payment.Data` y se obtuvo `Done.`.
- PostgreSQL confirmó las tablas `payments` y `__EFMigrationsHistory`, la clave primaria `PK_payments` y el índice **único** `payments_operation_id_key` sobre `OperationId`.
- `Payment.Grpc` arrancó con su configuración `ConnectionStrings:Payment` apuntando a la base migrada y escuchó en `http://localhost:5300` (HTTP/2).

### 5.3. Pruebas directas del servicio gRPC

Con un cliente temporal **fuera del repositorio** y autenticación Keycloak del cliente `booking`, se verificaron:

| Escenario | Evidencia observada |
|---|---|
| `Charge` exitoso, con importe `1000` centavos | `PaymentSucceeded` |
| Simulación de indisponibilidad, con importe `1033` centavos | `PaymentUnavailable` |
| `GetPayment` de la operación creada | Mismo pago y mismo estado |
| Repetición de `Charge` con el mismo `OperationId` y los mismos datos | Mismo `PaymentId`; no crea otro pago |
| Mismo `OperationId` con datos diferentes | gRPC `AlreadyExists` |
| Operación inexistente consultada con `GetPayment` | gRPC `NotFound` |

Antes de la prueba desde Booking, la base migrada contenía un pago `SUCCEEDED` y otro `UNAVAILABLE`.

### 5.4. Integración real Azure Service Bus → Booking → Payment → PostgreSQL

Se utilizó una **copia de trabajo separada de `feat/booking`** (`git worktree`), para no mezclar el código de Booking con el PR de Payment. Se inició Booking en `http://localhost:5200`, con PostgreSQL propio, Azure Service Bus configurado y Keycloak disponible.

Los logs de Booking demostraron, para una solicitud recibida desde la suscripción compartida `grupo5-booking-topic/booking`:

1. Booking recibió y procesó una solicitud de reserva, creó la reserva y la retuvo inicialmente (`Held`).
2. Booking obtuvo correctamente un token de Keycloak (`HTTP 200`).
3. `GrpcPaymentGateway` invocó `PaymentService/Charge` por gRPC (`HTTP 200`).
4. El registro de auditoría de Booking indicó **`END Charge … Outcome=Succeeded`**.
5. La lógica de Booking finalizó con **`ProcessReservationRequest -> Confirmed`**.
6. La consulta a PostgreSQL de Payment encontró el mismo `OperationId` **`5c66adb8-33da-4bc3-8fbd-3dff1f51b835`**, persistido con estado **`SUCCEEDED`**.

Tras esa prueba, en `payment_migrations_test` había **tres registros**: **dos `SUCCEEDED` y uno `UNAVAILABLE`**. Este dato incluye los dos pagos de pruebas directas anteriores, más el cobro efectuado desde Booking.

**Alcance comprobado:** recepción del mensaje por Booking y ejecución correcta del cobro hasta la persistencia en Payment. La solicitud fue tomada de una **suscripción compartida**; la evidencia no demuestra quién la publicó ni el recorrido desde Availability.

**Limitación detectada:** los logs registraron `ReservationConfirmed` mediante `LoggingReservationEventPublisher`, con el mensaje «sin publicador real configurado». Esto **no demuestra** que Notifications haya recibido un evento o enviado una notificación.

### 5.5. Revisión de código, aprobación e integración

- El revisor señaló un riesgo al recibir el mismo GUID con distintas combinaciones de mayúsculas/minúsculas. En `Charge` y `GetPayment` se normalizaron los identificadores con `Guid.Parse(...).ToString("D")` y se incorporaron pruebas de regresión (commit `a64a587`).
- Tras incorporar el código de negocio y las migraciones de Data, las **77 pruebas** de Payment pasaron localmente. La evidencia de ejecución se describe en los apartados anteriores; **no corresponde afirmar que estas pruebas se ejecutaron en un pipeline de CI**.
- Los archivos `docs/Payment_Arquitectura.md` y `prompts/Agustin_Vasino.md` se incorporaron inicialmente al PR mediante el commit `ac1b1b2`.
- El **05/11/2026**, Santi aprobó el [PR #27](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/27); posteriormente se hizo el **merge a `feat/payment`**, registrado como `a2daa2b`. La actualización documental posterior se prepara por separado, sin alterar el código ya aprobado.

### 5.6. Diferencia entre el fallo simulado y una caída real

La respuesta de negocio `PaymentUnavailable` obtenida con el simulador **no equivale** a interrumpir la disponibilidad del servidor gRPC. La prueba de caída real desde Booking (por ejemplo, un fallo de comunicación durante `Charge`) **no fue ejecutada como parte de estas verificaciones**. Corresponde al flujo alternativo de fallo de la entrega grupal, que requiere coordinación antes de usar la suscripción compartida del broker.
