## Prompt 1:
Contexto: el TP es un sistema de reserva de asientos con microservicios en .NET 10. El repo ya tiene la estructura base y Keycloak configurado.

Mi parte en Payment son los puntos 3e (autenticación gRPC contra Keycloak) y 3f (logs de auditoría con correlation id). El servicio de cobro queda fuera de mi alcance.

Antes de escribir código, explicame el flujo entre Booking, Keycloak y Payment, qué se valida en cada salto y qué archivos o capas voy a tocar. Después armame un plan de implementación paso a paso, con tests.

## Prompt 2:
Necesito implementar el punto 3e en Payment, que es un servidor gRPC en .NET 10. La autenticación la hace Keycloak con OAuth2 client credentials: Booking pide un token con aud=payment y lo manda en la metadata authorization: Bearer <token>.

Armame la configuración de JwtBearer en Payment.Grpc/Security validando firma contra el JWKS del realm, issuer, audiencia, expiración y ValidateIssuerSigningKey. Sumá ClockSkew de 30 segundos y MapInboundClaims = false para poder leer los claims con el nombre que emite Keycloak.

El issuer público es http://localhost:8081/realms/seat-reservation. Separá Authority de MetadataAddress en las options, y si MetadataAddress viene vacía, derivarla de Authority. Como Keycloak local usa HTTP, poné RequireHttpsMetadata = false. Además, Keycloak pone el client en el claim azp. No alcanza con que el token sea válido: tiene que estar emitido para booking. Hacelo con un IAuthorizationRequirement propio, no con un RequireClaim suelto, y aceptá client_id como fallback.

Usá FallbackPolicy para que cualquier MapGrpcService que se agregue después quede autenticado por defecto. El /health tiene que quedar AllowAnonymous. No implementes el servicio de cobro, eso queda fuera de mi alcance. Yo sólo dejo el host y la seguridad.

Que falle al arrancar con un InvalidOperationException claro si falta Authority, Audience o la lista Keycloak:AllowedClients (fail fast, no arrancar a medias). Todo configurable por la sección Keycloak de appsettings.json y por variables de entorno. Agregá el paquete Microsoft.AspNetCore.Authentication.JwtBearer a Payment.Grpc.

Dejá logs de JwtAuthenticationFailed y JwtChallenge sin registrar nunca el token.

## Prompt 3:
Seguimos con el punto 3f en Payment. Quiero un Interceptor de gRPC que marque el inicio y el fin de cada cadena de sucesos, pero sin repetir el código en cada método del servicio. Que sobrescriba el método UnaryServerHandler y envuelva la llamada completa.

El correlation id es el RequestId original de la reserva. Booking lo va a mandar en la metadata x-correlation-id. Ese envío es de Booking, no lo implementes en Payment. Aceptá también x-request-id como fallback y, si no viene ninguno, usá Activity.Current.TraceId o generá un GUID. Validá que no venga vacío, que no supere 128 caracteres y que no tenga caracteres de control, porque un cliente no puede inyectar saltos de línea en los logs.

Abrí un BeginScope con CorrelationId, ServiceName=Payment y ClientId sacado de azp (si no hay HttpContext o claim, unknown), así todos los logs de la operación quedan correlacionados. Escribí el header de respuesta antes de ejecutar el handler (después ya no se puede). Logueá START PaymentRequest y END PaymentRequest con Outcome, ElapsedMs y el método gRPC. Si hay RpcException no la transformes: registrá el StatusCode y relanzá, porque Booking trata un error técnico como resultado incierto y no como pago rechazado. Cualquier otra excepción: registrala como UnhandledException, logueá el error y relanzá. Configurá el logging de consola con IncludeScopes=true para que el scope se vea en los logs.

Poné el helper AuditLog en Payment.Business/Auditing con el mismo formato que usa Booking ([AUDIT] START/END ... ServiceName=... CorrelationId=...) para que la capa de negocio pueda loguear PaymentSucceeded/PaymentRejected con AuditLog.Step sin acoplarse a ASP.NET Core. No implementes esos eventos de negocio, sólo dejá el helper listo. Agregá Microsoft.Extensions.Logging.Abstractions a Payment.Business.

Registrá el interceptor como singleton y agregalo a AddGrpc(options => options.Interceptors.Add<...>()). EnableDetailedErrors sólo en Development. El contrato sólo tiene RPCs unary: dejá comentado que si mañana se agrega streaming hay que overridear los handlers correspondientes.

## Prompt 4:
Agregá un proyecto Payment.Tests con xUnit y referenciá Payment.Grpc. No quiero levantar Keycloak para correr los tests.

Debe cubrir:
AllowedClientHandler: azp=booking autoriza, otro client no, y client_id funciona como fallback.
CorrelationId.Resolve: lectura de x-correlation-id, fallback a x-request-id, rechazo de caracteres de control y de ids mayores a 128, y fallback al trace id o GUID.
AddPaymentAuthentication: que configure aud=payment, el issuer, MapInboundClaims = false, y que el FallbackPolicy exija el AllowedClientRequirement.
Que falle con un InvalidOperationException claro si falta Keycloak:Authority.

Usá ConfigurationBuilder.AddInMemoryCollection y IOptionsMonitor<JwtBearerOptions> para inspeccionar la configuración. Necesitás FrameworkReference a Microsoft.AspNetCore.App, Microsoft.NET.Test.Sdk, xunit y xunit.runner.visualstudio.

## Prompt 5:
Faltan tests del interceptor. No quiero levantar un servidor gRPC: armá un ServerCallContext falso (subclase con los miembros abstractos) y un ILogger<T> que capture los mensajes formateados.

Verificá:
Que una llamada unary escriba START y END con el correlation id que vino en x-correlation-id y Outcome=OK.
Que si no viene el header, genere un id de 32 caracteres hexadecimales (trace id o GUID).
Que una RpcException se loguee como Outcome=RpcError:<StatusCode> y se relance (no se transforme en otro error).

Si el interceptor explota con el contexto falso porque GetHttpContext() lanza InvalidOperationException fuera de ASP.NET Core, arreglalo: el logging no puede ser un punto de fallo de la llamada. Envolvelo para devolver null cuando no haya HttpContext.

## Prompt 6:
Payment.Grpc no acepta llamadas gRPC en el puerto 5300 porque Kestrel, sin TLS, usa HTTP/1.1 por defecto. Configurá Kestrel:EndpointDefaults:Protocols en Http2 dentro de appsettings.json para que el endpoint sea h2c puro. Los tests unitarios no levantan Kestrel, así que este cambio no afecta su resultado.
