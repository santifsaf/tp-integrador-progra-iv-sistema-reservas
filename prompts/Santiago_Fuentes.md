# Historial de prompts — Santiago Fuentes

## Prompt 01 

Antes de modificar archivos, verifica las interfaces actuales y presentame los archivos que propones crear o modificar. Espera mi autorizacion para implementar. 

## Prompt 02 

Implementa el endpoint de consulta del estado de una reserva por requestId en Booking, respetando la arquitectura N-Layer existente: Api → Business → Data.

Agrega GET /api/reservations/by-request/{requestId}, usando el identificador generado por Availability. El controlador debe delegar la consulta en un servicio de Booking.Business, que utilice el metodo existente IReservationRepository.GetByRequestIdAsync.

Si existe la reserva, devolve 200 con { status, description }. Los estados deben serializarse como HELD, CONFIRMED, REJECTED y EXPIRED. Si no existe, devolve 404 con ProblemDetails. Availability decidira si muestra la solicitud como pendiente; Booking no debe agregar un estado nuevo.

Diferencia la ausencia de reserva de un fallo tecnico: una excepcion del repositorio no debe convertirse en null ni en 404. La consulta debe limitarse a leer el estado persistido, sin ejecutar cobros ni transiciones.

Integra la validación JWT según la configuración de Keycloak del proyecto, propaga CancellationToken y agrega auditoría usando requestId como identificador de correlacion.

Reutiliza las interfaces y entidades existentes, respeta la separacion de responsabilidades entre capas y agrega pruebas del contrato HTTP y de los errores relevantes.

Una vez implementado, repasemos cada fragmento de codigo. 


### Correcciones
- El manejador de excepciones podia perder el requestId al leer los valores de ruta. Decidi recuperarlo mediante IExceptionHandlerFeature, para conservar la correlación en los logs, y agregar una prueba que lo verifique.



## Prompt 03

Implementa en Booking un proveedor de tokens para autenticarse ante Keycloak mediante client_credentials, utilizando el cliente booking.

Crea PaymentClientOptions para centralizar la direccion de Payment, el endpoint de tokens, las credenciales y los tiempos de espera. Incorpora la configuracion en appsettings.json y obtene el secreto mediante una variable de entorno.

El proveedor debe enviar las credenciales como formulario HTTP, leer access_token y comprobar que la respuesta contenga un token Bearer utilizable. Propaga CancellationToken y diferencia los errores HTTP de las respuestas invalidas.

Agrega pruebas de la solicitud enviada, la lectura del token, los errores y la cancelacion.


## Prompt 04

Implementa el componente de comunicacion de Booking con Payment utilizando el contrato compartido payment.proto y el proveedor de tokens preparado anteriormente.

Como Reservation todavia no contiene importe ni moneda, crea PaymentCharge para recibir explicitamente RequestId, ReservationId, PaymentOperationId, AmountCents de tipo long y Currency de tipo string.

Implementa Charge y GetPayment. Conserva el identificador de operacion recibido para permitir reintentos del mismo cobro. GetPayment debe limitarse a consultar, sin iniciar un cobro.

Envia el token como metadata de autorizacion y propaga RequestId mediante x-correlation-id. Configura deadline, timeout de autenticacion y CancellationToken.


### Correcciones

-Se corrigio la validacion de AmountCents para exigir un importe mayor que cero, de acuerdo con las restricciones del servidor Payment. Se agrego una prueba que comprueba que el importe cero se rechaza antes de solicitar el token o realizar una llamada gRPC.


## Prompt 05

Mapea las respuestas explicitas de Payment a los resultados existentes de Business. Los estados pendientes, desconocidos y errores de comunicacion deben producir Uncertain. Diferencia esos fallos de la cancelacion del solicitante, que debe propagarse.

Registra los componentes mediante inyeccion de dependencias, valida la configuracion y agrega pruebas de las llamadas, los resultados y los errores.


### Correcciones

- Se corrigio el diagnostico de los timeouts. Un deadline gRPC agotado puede producir OperationCanceledException sin que el solicitante haya cancelado. Se agrego el registro de la etapa (Authentication o Grpc) para identificar donde ocurrio el problema y una prueba que verifica el caso. El resultado continua siendo Uncertain, mientras que la cancelacion del solicitante se propaga.


## Prompt 06

Implementa ReservationPaymentClient como adaptador entre IPaymentClient y GrpcPaymentGateway, sin modificar la interfaz existente.

Agrega AmountCents y Currency a Reservation, sin asignar valores predeterminados. Utiliza Reservation.Id como PaymentOperationId para conservar la misma operación de cobro en los reintentos.

El adaptador debe delegar el cobro al gateway, propagar CancellationToken y devolver el resultado sin modificar el estado de la reserva. Los datos invalidos deben rechazarse antes de contactar a Payment.

Registra el adaptador mediante inyeccion de dependencias y actualiza los comentarios necesarios. No modifiques SQL ni el consumidor, ya que esas partes siguen pendientes de integracion.


## Prompt 07

Agrega pruebas para verificar que ReservationPaymentClient envíe correctamente los identificadores, el importe y la moneda, y que conserve los datos originales entre reintentos.

Comprueba que los resultados no modifiquen el estado de la reserva, que se propague la cancelación y que los datos inválidos se rechacen sin solicitar tokens ni realizar llamadas gRPC.

Verifica tambien el registro de IPaymentClient mediante inyección de dependencias y la validacion de la configuración.
