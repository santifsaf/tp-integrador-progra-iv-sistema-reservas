# Historial de prompts — Juan Pinto

**Trabajo práctico:** Programación IV — Sistema distribuido de reserva de asientos (UTN)  
**Componente:** Payment — lógica de negocio (`Payment.Business`), acceso a datos (`Payment.Data`), pruebas y base de datos  

## Alcance y criterio de selección

Este archivo reúne los prompts para implementar mi parte de Payment: entidad y contrato de la capa de negocio, `PaymentDbContext`, repositorio, simulador de pagos, `PaymentService`, pruebas, migración y la base de datos en Docker.


## 01. Contrato de la capa de negocio y entidad de pagos

### Prompt

> Mi capa Payment.Business tiene que exponerle a Payment.Grpc un contrato que no dependa de gRPC. Definí una interfaz `IPaymentService` con las operaciones de cobro y consulta de un pago, y también DTOs de entrada y salida, la entidad de pagos y el enum de estados. El dominio debe ser anémico, el importe debe guardarse como long y los casos de error deben comunicarse sin acoplar la capa de negocio a gRPC.

---

## 02. `PaymentDbContext` e índice único de idempotencia

### Prompt

> Armá el `PaymentDbContext` con EF Core y Npgsql para la entidad de pagos. Necesito que la base garantice que no se pueda registrar dos veces el mismo `OperationId`, incluso con solicitudes concurrentes, y que los estados se guarden como string.


---

## 03. Repositorio de pagos

### Prompt

> Implementá el repositorio de pagos con tres operaciones: buscar por `OperationId`, insertar y actualizar. La inserción no debe lanzar un error cuando el `OperationId` ya existe, sino indicarlo, y debe distinguir ese caso de cualquier otra violación de unicidad.

---

## 04. Simulador de pagos determinista

### Prompt

> Implementá un simulador de cobros que devuelva éxito, rechazo o pago no disponible de forma reproducible, separado de la lógica principal del servicio.

---

## 05. `PaymentService`: cobro idempotente

### Prompt

> Implementá `ChargeAsync` y `GetPaymentAsync` en `PaymentService`. Un reintento de Booking con el mismo `OperationId` no debe cobrar dos veces; debe poder consultarse el resultado de una operación aunque Booking haya superado su deadline; un pago que quedó sin resultado no debe quedar bloqueado para siempre; y la operación debe dejar logs de inicio y fin usando el `OperationId` como correlation id.

---

## 06. `PaymentService`: validación propia y conflictos concurrentes

### Prompt

> Cambiá `PaymentService` a partir de la review: la capa de negocio debe rechazar importes, monedas e identificadores inválidos sin depender de la validación gRPC, y si dos solicitudes concurrentes comparten `OperationId` con datos distintos, la que pierde debe recibir un conflicto en lugar del resultado de la otra. Evitá además un `NullReferenceException` poco claro.

---

## 07. Pruebas unitarias de `PaymentService`

### Prompt

> Creá pruebas unitarias para `PaymentService` usando un repositorio falso en memoria, sin base de datos. Deben cubrir los tres resultados del simulador, la idempotencia, el conflicto por datos distintos, el reintento de `UNAVAILABLE` y de un `PENDING` huérfano, la consulta con `GetPayment`, la validación de entrada y una carrera concurrente simulada.

---

## 08. Pruebas de integración del repositorio contra PostgreSQL real

### Prompt

> Necesito comprobar que los reintentos con el mismo `OperationId` no generan registros duplicados y que un choque de otra restricción única no se confunde con un reintento. Probémoslo contra un PostgreSQL real usando Docker.

---

## 09. Migración inicial y base de datos de Payment en Docker

### Prompt

> Generá la migración inicial de EF Core para la base de Payment sin depender del `Program.cs` de `Payment.Grpc`, y sin dejar credenciales fijas en el código. Agregá el servicio PostgreSQL de Payment al `docker-compose` siguiendo el patrón de las otras bases y documentá cómo configurarlo y aplicar la migración.