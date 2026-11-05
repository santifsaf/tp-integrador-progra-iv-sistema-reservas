# Historial de prompts e interacciones con IA — Agustín Vasino

**Trabajo práctico:** Programación IV — Sistema distribuido de reserva de asientos (UTN)  
**Componente:** Payment gRPC — capa de presentación e integración con Business  
**Herramienta:** ChatGPT  
**Rama de trabajo:** `feat/payment-agus`  
**Pull requests:** [#4 — Contrato gRPC](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/4) y [#27 — Servidor gRPC (borrador)](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/27)  
**Período documentado:** 30/10/2026–04/11/2026  
**Estado del documento:** Actualizado al 04/11/2026; resta completar la integración definitiva con Booking y el cierre del PR #27.

## Alcance y criterio de selección

Este archivo reúne **solicitudes técnicas e intercambios relevantes** de la asistencia con IA durante el desarrollo individual. Se excluyen confirmaciones breves (por ejemplo, «listo» o «sigamos»), consultas administrativas repetidas e indicaciones mecánicas que no modificaron decisiones técnicas.

**Fidelidad de los prompts:** las entradas identificadas como **síntesis** describen el objetivo de intercambios reales, pero **no son transcripciones literales**. Las entradas identificadas como **cita textual** sí reproducen mensajes disponibles en el historial. Esta distinción evita presentar una reformulación como un prompt originalmente escrito. Los resultados se contrastaron con los commits y archivos del repositorio.

**Alcance individual:** se documenta la implementación y prueba de la capa gRPC, su integración y la revisión técnica de cambios compartidos. El repositorio y la lógica de negocio de pagos fueron desarrollados por Juan; la autenticación y auditoría base fueron desarrolladas por Kevin. Se mencionan solamente cuando su integración o verificación estuvo relacionada con el trabajo individual.

---

## Etapa 1 — Contrato y estructura del servidor gRPC

### 01. Análisis del componente Payment y del contrato con Booking

- **Fecha:** octubre de 2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** analizar la consigna del TP y definir el contrato gRPC compartido para que Booking solicite un cobro y consulte el resultado de una operación, respetando la arquitectura N-Layer y la separación de microservicios.
- **Resultado:** contrato `contracts/proto/payment.proto` con los RPC `Charge` y `GetPayment`, mensajes de solicitud/respuesta y estados de pago.
- **Evidencia:** [PR #4](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/4), commit `0604044`.

### 02. Generación de clases y esqueleto de `PaymentGrpcService`

- **Fecha:** 01–02/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** preparar el proyecto `.NET` para generar los tipos C# desde `payment.proto`, crear el servidor gRPC y registrar su endpoint en el host sin incorporar lógica de negocio en la presentación.
- **Resultado:** configuración de `Payment.Grpc.csproj`, clase `PaymentGrpcService` y registro del servicio en `Program.cs`.
- **Evidencia:** commits `a44bbff`, `16506ae` y `459583b`.

### 03. Validaciones de entrada en `Charge` y `GetPayment`

- **Fecha:** 01–02/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** determinar qué solicitudes deben responder `INVALID_ARGUMENT`, validando identificadores GUID no vacíos, importes mayores a cero y moneda de tres letras mayúsculas.
- **Resultado:** validaciones en `PaymentGrpcService.cs` antes de delegar a Business; mensajes de error adecuados al contrato.
- **Evidencia:** commit `a66f713`.

### 04. Pruebas automatizadas de las validaciones gRPC

- **Fecha:** 02/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** comprobar mediante xUnit las entradas inválidas, los códigos gRPC y el recorrido de las solicitudes válidas sin depender de una base de datos.
- **Resultado:** `PaymentGrpcServiceValidationTests.cs`, con escenarios parametrizados para los campos principales.
- **Evidencia:** commit `c2afc6b`.

## Etapa 2 — Integración con componentes del equipo

### 05. Integración y comprobación de autenticación y auditoría

- **Fecha:** 02–03/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** incorporar los cambios ya desarrollados por otro integrante para seguridad y auditoría; verificar el servidor con Keycloak, JWT, HTTP/2 y llamadas gRPC de prueba.
- **Resultado:** integración del trabajo de Kevin en la rama personal, registro del servicio y comprobación de respuestas de autenticación/validación. **La implementación de Keycloak y los interceptores no se atribuye al autor de este historial.**
- **Evidencia:** commits `c9877fd` y `459583b`; [PR #9 de Kevin](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/9).

### 06. Adaptador de modelos: `PaymentGrpcMapper`

- **Fecha:** 03/11/2026 (trabajo local; commit publicado el 04/11 UTC).
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** diseñar una conversión explícita entre `ChargeRequest` de protobuf, `ChargeRequestDto` de Business y `PaymentResultDto`, incluyendo el mapeo de todos los estados de pago y valores opcionales.
- **Resultado:** `PaymentGrpcMapper.cs` y `PaymentGrpcMapperTests.cs`, con pruebas del contenido de las solicitudes y de las conversiones de estados.
- **Evidencia:** commit `b5208ae`.

### 07. Revisión de idempotencia y errores del repositorio compartido

- **Fecha:** 03–04/11/2026.
- **Tipo:** síntesis de una revisión técnica; el texto exacto de la solicitud original queda pendiente de cotejo.
- **Solicitud trabajada:** revisar el PR #21 de Juan y sus casos de error e idempotencia.
- **Contexto y resultado:** al revisar `PaymentRepository.TryAddAsync`, se detectó que el filtro de PostgreSQL capturaba cualquier violación de unicidad. Se recomendó comprobar `ConstraintName` para distinguir el conflicto por `OperationId` de otros errores; Juan incorporó la corrección. También se pidió cubrir los reintentos con pruebas de integración.
- **Evidencia:** [PR #21](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/21), commit de Juan `7539948`; [PR #23](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/23). **La implementación y las pruebas del repositorio pertenecen a Juan.**

## Etapa 3 — Conexión del servidor con Business y depuración

### 08. Inyección de `IPaymentService` y adaptación de los RPC

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** sustituir las respuestas provisionales `UNIMPLEMENTED` por llamadas asincrónicas a la interfaz `IPaymentService`, conservar las validaciones existentes y reutilizar el mapper entre gRPC y Business.
- **Resultado:** `PaymentGrpcService` recibe `IPaymentService` por constructor; `Charge` llama a `ChargeAsync` y `GetPayment` a `GetPaymentAsync`.
- **Evidencia:** commit `aa8fde3`, [PR #27](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/27).

### 09. Resolución de conflicto entre enumeraciones `PaymentStatus`

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis de una consulta de depuración; el texto exacto queda pendiente de cotejo.
- **Solicitud trabajada:** investigar por qué `PaymentStatus` aparecía marcado como error en las pruebas.
- **Contexto y resultado:** el archivo de pruebas importaba dos enumeraciones llamadas `PaymentStatus`, una de Data y otra del contrato gRPC. Se resolvió mediante un alias para el tipo de Business, sin modificar los contratos compartidos.
- **Archivo:** `PaymentGrpcServiceValidationTests.cs`.

### 10. Corrección de validación de `GetPayment`

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** investigar tres pruebas que fallaban porque `GetPayment` aceptaba identificadores inválidos tras modificar su implementación.
- **Resultado:** se restauró la validación del GUID antes de consultar Business. Las 40 pruebas existentes volvieron a pasar.
- **Evidencia:** ejecución local de `dotnet test`, con resultados compartidos en la conversación.

### 11. Manejo del conflicto `PaymentConflictException`

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** traducir a un estado gRPC específico el caso de reutilización de `payment_operation_id` con datos incompatibles; agregar una prueba de regresión mediante un servicio simulado.
- **Resultado:** `PaymentConflictException` se transforma en `ALREADY_EXISTS`. Ante una primera falla del test se revisó el bloque `try/catch`, se comprobó el código guardado y se hizo una compilación limpia antes de repetir las pruebas.
- **Evidencia:** commit `aa8fde3`; 41 pruebas correctas tras la corrección y el rebuild.

### 12. Consulta de una operación inexistente

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** comprobar que, cuando `IPaymentService.GetPaymentAsync` devuelve `null`, el endpoint de consulta responda `NOT_FOUND` en vez de producir una respuesta de pago incorrecta.
- **Resultado:** manejo explícito de resultado inexistente y prueba usando `FakePaymentService`.
- **Evidencia:** commit `aa8fde3`; 42 pruebas correctas.

### 13. Verificación de los datos enviados a Business

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** capturar la solicitud recibida por el fake de Business para comprobar que `ReservationId`, `PaymentOperationId`, `AmountCents` y `Currency` se transmiten sin modificaciones indebidas.
- **Resultado:** prueba `Charge_ValidRequest_SendsCorrectDataToBusiness`, que verifica el DTO enviado a la interfaz de negocio.
- **Evidencia:** commit `abcea70`.

## Etapa 4 — Integración de ramas y verificación

### 14. Integración progresiva y pruebas con PostgreSQL

- **Fecha:** 03–04/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** revisar PR de Business/Data, incorporar a la rama individual únicamente los cambios integrados en `feat/payment` y verificar compilación y tests antes de publicar.
- **Resultado:** merges controlados de los PR #11, #18, #21 y #23. Se ejecutó `dotnet build SeatReservation.sln` y `dotnet test src/Payment/Payment.Tests/Payment.Tests.csproj`. Los dos tests de repositorio de Juan se ejecutaron con Testcontainers/PostgreSQL.
- **Evidencia:** commits de merge `863a275`, `23b41cb` y `d5067b6`. Se obtuvieron **45 pruebas correctas, 0 errores** en la última ejecución local comunicada (incluye pruebas propias y de otros integrantes).

### 15. Preparación del pull request individual

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis documental del intercambio técnico, no cita textual.
- **Solicitud trabajada:** publicar el desarrollo individual en un PR de `feat/payment-agus` hacia `feat/payment`, conservándolo como borrador hasta completar la conexión con la implementación concreta de Business.
- **Resultado:** [PR #27, Draft](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/27), con alcance, evidencias de pruebas y tareas pendientes documentadas.

---

## Etapa 5 — Integración local de `PaymentService` con gRPC

### 16. Revisión del PR #28 y detección de un caso de concurrencia

- **Fecha:** 04/11/2026.
- **Tipo:** cita textual verificable del mensaje que inició esta revisión.
- **Prompt original:** «juan me mando esto fijate si podemos seguir adelante con nuestra parte».
- **Objetivo y contexto:** inspeccionar la implementación de `PaymentService` que Juan publicó mediante el PR #28, revisar el contrato y comprobar cómo se mapearían sus errores desde gRPC.
- **Resultado:** se identificó la nueva `PaymentValidationException` y la necesidad de mapearla a `INVALID_ARGUMENT`. Se detectó también un riesgo en el camino concurrente de `ChargeAsync`: si `TryAddAsync` falla por un `OperationId` duplicado, el registro encontrado no se contrasta con los datos de la segunda solicitud.
- **Evidencia:** [PR #28](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/28). La corrección de ese escenario corresponde a Juan, no al autor de este historial.

### 17. Comunicación de la observación en la revisión de código

- **Fecha:** 04/11/2026.
- **Tipo:** cita textual verificable en la conversación.
- **Prompt original:** «si dejale el comentario».
- **Objetivo y resultado:** publicar en el PR #28 la observación sobre el conflicto de importes/reservas en solicitudes concurrentes con un mismo `OperationId`; solicitar una prueba que reproduzca el caso y tratamiento explícito del posible valor `null` en lugar de `winner!`.
- **Evidencia:** [comentario de revisión en el PR #28](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/28#issuecomment-6642165118).

### 18. Rama temporal, incorporación controlada y diagnóstico de pruebas

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis de una sesión de trabajo guiada, no cita literal de un único prompt.
- **Solicitud trabajada:** incorporar el código aún no integrado de Juan solo para verificar compatibilidad, sin afectar `feat/payment-agus` ni modificar los archivos de Business/Data.
- **Procedimiento:** creación local de `feat/payment-integracion-agus`; `git fetch origin`; `git merge origin/feat/payment-juan`, sin conflictos.
- **Diagnóstico:** primera ejecución de 70 pruebas: 68 correctas y 2 con `DockerUnavailableException` porque Docker Desktop no estaba ejecutándose. Al iniciar Docker y repetirlas, se informaron 70 pruebas correctas.
- **Resultado:** entorno local de integración funcional; el merge de Juan en esta rama es **experimental**, no equivale a aprobar su PR.

### 19. Manejo de `PaymentValidationException` en gRPC y prueba de regresión

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis de interacción técnica y cambios revisados paso a paso.
- **Solicitud trabajada:** capturar `PaymentValidationException` de Business dentro de `Charge`, responder `RpcException` con `StatusCode.InvalidArgument` y comprobar que la validación propia de Business queda diferenciada de la validación de presentación.
- **Resultado:** incorporación del `catch` correspondiente; el fake de `IPaymentService` recibió una opción para lanzar la excepción y se agregó el test `Charge_BusinessValidationException_ReturnsInvalidArgument`.
- **Verificación:** **71 pruebas correctas**, sin fallos, en la ejecución local comunicada.
- **Archivos propios:** `PaymentGrpcService.cs`, `PaymentGrpcServiceValidationTests.cs`.

### 20. Registro de dependencias para ejecutar Business y Data reales

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis de interacción técnica; no cita literal.
- **Solicitud trabajada:** reemplazar el servicio simulado de las pruebas por los componentes concretos ya implementados, sin introducir lógica de negocio en el host gRPC.
- **Resultado:** referencia de `Payment.Grpc` a `Payment.Data`, registro de `PaymentDbContext` con Npgsql, `IPaymentRepository`/`PaymentRepository`, `IPaymentService`/`PaymentService` y `PaymentSimulator` en `Program.cs`. La cadena de conexión se obtiene de `ConnectionStrings:Payment`.
- **Verificación:** compilación correcta de `Payment.Grpc` y arranque del host en el puerto local 5300.
- **Archivos propios:** `Payment.Grpc.csproj`, `Program.cs`.

### 21. Preparación de PostgreSQL para una integración local reproducible

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis de interacción técnica; no cita literal.
- **Solicitud trabajada:** preparar una base de datos PostgreSQL de Payment sin alterar el Docker Compose compartido ni la base de datos de Booking.
- **Resultado:** contenedor local de PostgreSQL 17 con puerto host `5434`; conexión de Payment por variable de entorno; ejecución de una aplicación temporal externa al repositorio para crear el esquema a partir de `PaymentDbContext` con `EnsureCreatedAsync`. Se verificó la existencia de la tabla `public.payments`.
- **Limitación reconocida:** esta preparación **no genera migraciones de EF Core** ni es una configuración definitiva del grupo. Antes de entregar hay que acordar y aplicar migraciones oficiales. No se registran credenciales privadas ni tokens en el historial.

### 22. Diagnóstico de HTTP/2 y prueba de disponibilidad del host

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis de depuración.
- **Problema observado:** `curl.exe` de Windows envió HTTP/1.1 a un endpoint configurado solo para HTTP/2; la prueba de `/health` respondió con un error de protocolo. La consola Windows PowerShell utilizada no tenía disponibles los tipos esperados para el primer intento con `HttpClient`.
- **Solución de prueba:** crear una aplicación temporal .NET 10 fuera del repositorio que utilizara HTTP/2 explícitamente.
- **Resultado:** respuesta de `/health` recibida correctamente con `HTTP 200` y el cuerpo de estado de Payment. No fue necesario relajar la configuración HTTP/2 de gRPC.

### 23. Autenticación con Keycloak y primer cobro con persistencia real

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis de interacción técnica y resultados observados.
- **Solicitud trabajada:** usar un token `client_credentials` emitido para el cliente `booking` y realizar `Charge` contra el servidor gRPC local.
- **Resultado:** cliente temporal .NET con `Grpc.Net.Client`, `Google.Protobuf` y clases generadas desde el mismo `payment.proto`; token pasado por una variable de entorno, sin copiar su contenido al repositorio.
- **Verificación:** `Charge` devolvió `PaymentSucceeded` al simular un importe de 1000 centavos; la tabla `payments` contenía el mismo `PaymentId` y `OperationId`, estado `SUCCEEDED`, moneda `ARS` e importe 1000.
- **Nota de alcance:** se probó el cliente temporal autenticado como Booking; **todavía no se ejecutó el proceso real de Booking** en esta prueba.

### 24. Recuperación de pagos, idempotencia y traducción de errores

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis de pruebas de integración locales; no cita literal de un único prompt.
- **Solicitud trabajada:** verificar el comportamiento observable de `GetPayment` y el manejo de reintentos y errores desde un cliente gRPC autenticado.
- **Resultados:**
  - `GetPayment` recuperó el `PaymentId` generado por `Charge` sin iniciar un pago nuevo.
  - Repetir `Charge` con el mismo `OperationId` y los mismos datos devolvió el mismo `PaymentId`; se verificó un solo registro por operación en PostgreSQL.
  - Repetir el `OperationId` con un importe diferente, **después de creado el primer pago**, devolvió `ALREADY_EXISTS`.
  - Consultar un `OperationId` válido e inexistente devolvió `NOT_FOUND`.
- **Límite de la prueba:** el caso secuencial de `ALREADY_EXISTS` **no sustituye** la prueba de concurrencia solicitada a Juan en el PR #28.

### 25. Resultados rechazado/no disponible y renovación de credenciales temporales

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis de pruebas y depuración.
- **Solicitud trabajada:** verificar los resultados no exitosos del simulador a través del servidor gRPC y confirmar que también pueden consultarse y reintentarse sin duplicar la operación.
- **Resultados observados:**
  - Importe de 1099 centavos: `PaymentRejected` con motivo `Pago Fallido`; `GetPayment` y el reintento conservaron el identificador de pago.
  - Importe de 1033 centavos: `PaymentUnavailable` con motivo `Pago no disponible`; `GetPayment` y el reintento conservaron el identificador de pago.
  - Un primer intento devolvió `Unauthenticated`/HTTP 401; se obtuvo un token nuevo de Keycloak y la prueba volvió a funcionar.
- **Alcance:** simulación determinista de resultados de negocio, no una pasarela de pagos externa.

### 26. Revisión final de los cambios y commit de integración local

- **Fecha:** 04/11/2026.
- **Tipo:** síntesis del control de versiones realizado.
- **Procedimiento:** `git status`, `git diff --check`, selección explícita de cuatro archivos propios y commit local.
- **Resultado:** commit `9d2efa2` — `feat(payment): integrar dependencias y validacion de negocio en gRPC`, sobre la rama temporal `feat/payment-integracion-agus`.
- **Archivos incluidos:** `Payment.Grpc.csproj`, `Program.cs`, `PaymentGrpcService.cs` y `PaymentGrpcServiceValidationTests.cs`.
- **Estado de publicación:** el commit no se había enviado a GitHub al cierre de esta actualización; el PR #27 seguía como borrador. El archivo de prompts tampoco se había agregado al repositorio.

---

## Resultados verificados a la fecha

- Contrato compartido `payment.proto` integrado mediante el PR #4.
- Servidor gRPC con validaciones y RPC `Charge`/`GetPayment` delegados a `IPaymentService`.
- Mapper de solicitudes y respuestas entre protobuf y Business.
- Traducción de errores de negocio a `ALREADY_EXISTS` y `INVALID_ARGUMENT`, y operaciones inexistentes a `NOT_FOUND`.
- **71 pruebas automatizadas correctas** en la última ejecución local comunicada del proyecto Payment; incluyen pruebas de diversos integrantes y utilizan Testcontainers para las de repositorio.
- Host de Payment compilado e iniciado con la implementación concreta de Business y Data integrada en la rama temporal.
- Cobros simulados `SUCCEEDED`, `REJECTED` y `UNAVAILABLE` comprobados mediante cliente gRPC autenticado, con Keycloak y PostgreSQL locales.
- `GetPayment`, idempotencia secuencial, conflicto por datos distintos y consulta inexistente comprobados.
- Commit individual publicado previamente: `abcea70` en `feat/payment-agus`; **commit local pendiente de publicación**: `9d2efa2` en `feat/payment-integracion-agus`.
- [PR #27](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/27) abierto como borrador al cierre de esta actualización.

## Pendientes de integración y documentación

1. Verificar la corrección del caso concurrente en el [PR #28 de Juan](https://github.com/santifsaf/tp-integrador-progra-iv-sistema-reservas/pull/28) y aprobar únicamente después de la revisión correspondiente.
2. Incorporar a nuestra rama de publicación la versión aprobada de Business/Data y nuestro commit de integración sin arrastrar cambios ajenos innecesarios.
3. Preparar y aplicar las **migraciones oficiales de EF Core** y la configuración de infraestructura de Payment para el entorno compartido; `EnsureCreatedAsync` se usó solo en la base local de prueba.
4. Probar el recorrido real **Booking → Payment gRPC → Business → PostgreSQL**, así como el flujo de fallo solicitado por la consigna cuando un componente no esté disponible.
5. Actualizar el PR #27 y completar el historial con cualquier prompt o corrección técnica adicional antes de entregar.

**Nota metodológica:** las entradas de tipo *síntesis* no son citas textuales de los prompts; distinguen claramente el pedido técnico, el trabajo efectuado y la evidencia disponible. Si se exige una exportación literal completa, será necesario adjuntar por separado las conversaciones originales. No se incluyen secretos ni tokens.
