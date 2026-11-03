# Especificación técnica — Availability · Capa Infrastructure
# Especificación técnica — Availability · Capa Infrastructure

**Proyecto:** TP Final — Sistema de reserva de asientos (grupo 5)
**Basado en:** `product-brief.md` y `business-rules.md` (2026-10-31)
**Responsable:** Rebeca — capa Infrastructure
**Última actualización:** 2026-10-31

## Cómo leer este documento

- Lo que figura como **confirmado** viene del product brief, de las reglas de negocio o de lo que pasó el equipo (docker-compose, instructivo de Keycloak).
- Lo que figura como **propuesta técnica** es una recomendación mía para implementar algo que el equipo todavía no definió. Se confirma o se cambia.
- Lo que figura como **pendiente** no se asumió: falta un dato o una decisión (sección 13).
- Los esqueletos de código muestran la estructura y las decisiones, no son código terminado. Los comentarios en español siguen el criterio de la consigna: justificar patrones y decisiones.

---

## 1. Alcance

La capa Infrastructure de Availability implementa las interfaces que declara Application:

| Interfaz (Application) | Implementación propuesta | Tecnología |
|---|---|---|
| `IReservationRequestRepository` | `ReservationRequestRepository` | EF Core + PostgreSQL |
| `IEventRepository` | `EventRepository` | EF Core + PostgreSQL |
| `ISeatRepository` | `SeatRepository` | EF Core + PostgreSQL |
| `IReservationPublisher` | `ServiceBusReservationPublisher` | Azure Service Bus |
| `IBookingClient` | `BookingClient` | `HttpClient` + token de Keycloak |

Fuera de alcance: casos de uso, DTO de Application y entidades (Feli), endpoints y validación del JWT del usuario (Luciana), Keycloak y docker-compose (Felipe), lógica de Booking.

## 2. Stack y versiones

| Elemento | Valor | Estado |
|---|---|---|
| Framework | `net10.0` | Confirmado |
| Base de datos | PostgreSQL, una base por servicio | Confirmado |
| Versión de la imagen de PostgreSQL | `postgres:16` para Availability | Acordado (2026-11-03) |
| ORM y provider | EF Core con `Npgsql.EntityFrameworkCore.PostgreSQL` | Confirmado |
| Broker | Azure Service Bus (topic `grupo5-booking-topic`, suscripción `booking`) | Confirmado |
| Servidor de autorización | Keycloak 26.4, realm `seat-reservation`, puerto `8081` | Confirmado |
| Resiliencia HTTP | `Microsoft.Extensions.Http.Resilience` (basado en Polly) | Propuesta técnica |
| Resiliencia del publisher | Reintentos del propio SDK de Service Bus | Propuesta técnica (ver sección 8) |
| Logging | Serilog con logs estructurados a consola; consumo mediante `ILogger<T>` | Convención acordada (2026-11-03); falta integrar el formato y middleware en Api |
| Framework de pruebas | — | Pendiente |

Las versiones exactas de los paquetes se fijan al agregarlos, usando la última estable compatible con .NET 10.

## 3. Estructura y dependencias

```
Availability.Infrastructure/
├── Persistence/
│   ├── AvailabilityDbContext.cs
│   ├── Configurations/
│   │   ├── ReservationRequestConfiguration.cs
│   │   ├── EventConfiguration.cs
│   │   └── SeatConfiguration.cs
│   └── Migrations/
├── Repositories/
│   ├── ReservationRequestRepository.cs
│   ├── EventRepository.cs
│   └── SeatRepository.cs
├── Messaging/
│   ├── ServiceBusReservationPublisher.cs
│   └── ReservationRequestedMessage.cs
├── Http/
│   ├── BookingClient.cs
│   ├── BookingAuthHandler.cs
│   ├── CorrelationIdHandler.cs
│   └── KeycloakTokenProvider.cs
├── Options/
│   ├── ServiceBusOptions.cs
│   ├── BookingClientOptions.cs
│   └── KeycloakOptions.cs
├── DatabaseInitializer.cs
└── DependencyInjection.cs
```

**Referencias entre proyectos** (regla de dependencia de Clean Architecture, BR-35):

- `Availability.Infrastructure` referencia `Availability.Application` y `Availability.Domain`.
- Application y Domain no referencian a Infrastructure.

**Paquetes** (versiones por definir, ver sección 2):

| Proyecto | Paquete |
|---|---|
| Infrastructure | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Infrastructure | `Azure.Messaging.ServiceBus` |
| Infrastructure | `Microsoft.Extensions.Http.Resilience` |
| Api (ejecutable) | `Microsoft.EntityFrameworkCore.Design`, solo para correr `dotnet ef` |

## 4. Configuración

Todo valor que cambia según el entorno va en configuración, nunca escrito en el código (BR-31). En contenedores se pasan como variables de entorno, donde `__` reemplaza a `:`.

| Clave | Variable de entorno | Contenido | Estado |
|---|---|---|---|
| `ConnectionStrings:AvailabilityDb` | `ConnectionStrings__AvailabilityDb` | Docker: host `availability-db`, puerto `5432`; local: host `localhost`, puerto `5433` | Variables `AVAILABILITY_DB_NAME`, `AVAILABILITY_DB_USER`, `AVAILABILITY_DB_PASSWORD` en `.env`; el servicio DB ya está en compose |
| `ServiceBus:ConnectionString` | `ServiceBus__ConnectionString` | Connection string del broker (la entrega el profesor) | Confirmado; es un secreto |
| `ServiceBus:TopicName` | `ServiceBus__TopicName` | `grupo5-booking-topic` | Confirmado |
| `Booking:BaseUrl` | `Booking__BaseUrl` | Docker: `http://booking:8080/`; local: `http://localhost:5200/` | Acordado; falta configurar en Api |
| `Keycloak:TokenEndpoint` | `Keycloak__TokenEndpoint` | Docker: `http://keycloak:8081/realms/seat-reservation/protocol/openid-connect/token`; local: `http://localhost:8081/realms/seat-reservation/protocol/openid-connect/token` | Acordado; falta configurar en Api |
| `Keycloak:ClientId` | `Keycloak__ClientId` | `availability` | Confirmado |
| `Keycloak:ClientSecret` | `Keycloak__ClientSecret` | Secreto del client `availability`, tomado de `${AVAILABILITY_CLIENT_SECRET}` por Compose | Convención acordada; falta agregar el servicio de la API a compose |

**Nota sobre el token endpoint.** Desde la máquina del desarrollador se usa `localhost`; desde el contenedor se usa el DNS de Compose `keycloak`. Felipe confirmó que el emisor (`iss`) sigue siendo `http://localhost:8081/realms/seat-reservation`.

La base de Availability publica `5433:5432`: la API ejecutada en el host usa `localhost:5433`; una API dentro de Compose usa `availability-db:5432`. Al integrar la API en Compose, deberá esperar a `availability-db` con `depends_on: condition: service_healthy`.

**Secretos.** El compose toma los secretos de las variables `${...}` de un archivo `.env` que no se versiona. El repositorio solo contiene `.env.example` con los nombres de las variables y sin valores reales.

## 5. Persistencia (PostgreSQL + EF Core)

### 5.1 `AvailabilityDbContext`

```csharp
// Availability.Infrastructure/Persistence/AvailabilityDbContext.cs
public class AvailabilityDbContext : DbContext
{
    public AvailabilityDbContext(DbContextOptions<AvailabilityDbContext> options) : base(options) { }

    public DbSet<ReservationRequest> ReservationRequests => Set<ReservationRequest>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Seat> Seats => Set<Seat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Fluent API en clases separadas (BR-37): el dominio queda anémico y sin
        // referencias a EF Core, como exige Clean Architecture (BR-35, BR-36).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AvailabilityDbContext).Assembly);
    }
}
```

### 5.2 Configuración de las entidades

```csharp
public class ReservationRequestConfiguration : IEntityTypeConfiguration<ReservationRequest>
{
    public void Configure(EntityTypeBuilder<ReservationRequest> builder)
    {
        builder.ToTable("ReservationRequests");

        // La clave primaria garantiza que no haya dos solicitudes con el mismo Id (BR-08).
        builder.HasKey(x => x.Id);
        // El Id lo genera Application, no la base (BR-06).
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.SeatId).IsRequired();

        // Se guarda como texto legible: "Received", "Published", "PublishFailed" (BR-10).
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Npgsql solo acepta DateTime con Kind = Utc en columnas timestamptz (BR-07, BR-38).
        builder.Property(x => x.CreatedAt).IsRequired();

        // Para GetByUserIdAsync (BR-14).
        builder.HasIndex(x => x.UserId);
    }
}

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StartsAt).IsRequired();

        builder.Property(x => x.PriceCents).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();

        // Seed con GUID fijos, nunca Guid.NewGuid(): si cambiaran en cada migración
        // se duplicarían los datos (BR-04). Los valores concretos están pendientes.
        // builder.HasData(new Event
        // {
        //     Id = Guid.Parse("..."),
        //     Name = "...",
        //     StartsAt = new DateTime(2026, 12, 1, 20, 0, 0, DateTimeKind.Utc),
        //     PriceCents = 15000,
        //     Currency = "ARS"
        // });
    }
}

public class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("Seats");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Label).HasMaxLength(20).IsRequired();

        // Todo asiento pertenece a un evento (BR-02).
        builder.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);

        // Para listar los asientos de un evento y verificar el par evento + asiento (BR-01).
        builder.HasIndex(x => x.EventId);
    }
}
```

### 5.3 Repositorios

```csharp
public class ReservationRequestRepository : IReservationRequestRepository
{
    private readonly AvailabilityDbContext _db;
    public ReservationRequestRepository(AvailabilityDbContext db) => _db = db;

    // Solo persiste lo que recibe, sin completar datos (BR-09); guarda por sí mismo (BR-12).
    public async Task SaveAsync(ReservationRequest request, CancellationToken ct)
    {
        _db.ReservationRequests.Add(request);
        await _db.SaveChangesAsync(ct);
    }

    // Modifica únicamente el estado (BR-11). ExecuteUpdateAsync hace un UPDATE directo.
    public async Task UpdateStatusAsync(Guid id, RequestStatus status, CancellationToken ct)
    {
        var rows = await _db.ReservationRequests
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status), ct);

        if (rows == 0) throw new KeyNotFoundException($"No existe la solicitud {id}.");
    }

    // Devuelve null si no existe (BR-13). AsNoTracking: es lectura.
    public Task<ReservationRequest?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.ReservationRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    // Solo las del usuario, más recientes primero; Id desempata para un orden estable (BR-14, BR-42).
    public async Task<IReadOnlyList<ReservationRequest>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        await _db.ReservationRequests.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
}

public class SeatRepository : ISeatRepository
{
    private readonly AvailabilityDbContext _db;
    public SeatRepository(AvailabilityDbContext db) => _db = db;

    // Orden por etiqueta; Id desempata para un orden estable (BR-42).
    public async Task<IReadOnlyList<Seat>> GetByEventAsync(Guid eventId, CancellationToken ct) =>
        await _db.Seats.AsNoTracking().Where(s => s.EventId == eventId)
            .OrderBy(s => s.Label).ThenBy(s => s.Id).ToListAsync(ct);

    // Se verifica el PAR evento + asiento (BR-01).
    public Task<bool> ExistsAsync(Guid eventId, Guid seatId, CancellationToken ct) =>
        _db.Seats.AsNoTracking().AnyAsync(s => s.Id == seatId && s.EventId == eventId, ct);
}

// EventRepository: GetAllAsync y GetByIdAsync son de solo lectura con AsNoTracking (BR-03).
// GetAllAsync ordena por StartsAt ascendente e Id ascendente (BR-42).
```

El catálogo es de solo lectura (BR-03): ninguno de estos repositorios tiene operaciones de escritura sobre `Events` ni `Seats`.

### 5.4 Migraciones y arranque

**Confirmado:** las migraciones se aplican automáticamente al iniciar la app. Como el seed está en `HasData`, se carga junto con las migraciones.

```csharp
// Availability.Infrastructure/DatabaseInitializer.cs
public static class DatabaseInitializer
{
    // Equivalente asíncrono de Database.Migrate(). Lo llama Program.cs del Api (Luciana).
    public static async Task ApplyMigrationsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AvailabilityDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}

// Program.cs (Api): await app.Services.ApplyMigrationsAsync();
```

Puntos a tener en cuenta:

- **Cambios en el modelo:** desde EF Core 9, `Migrate()` falla si el modelo tiene cambios sin migración. Cada cambio en una entidad o configuración necesita una migración nueva.
- **Una sola persona genera las migraciones** (BR-40).
- **Orden de arranque:** si el contenedor de Availability inicia antes de que PostgreSQL esté listo, la migración falla. Se resuelve con un `depends_on` con healthcheck en el compose (Felipe) y, como propuesta técnica, con `EnableRetryOnFailure()` en las opciones de Npgsql.
- **Generar una migración:**

```bash
dotnet ef migrations add InitialCreate \
  --project src/Availability/Availability.Infrastructure \
  --startup-project src/Availability/Availability.Api \
  --output-dir Persistence/Migrations
```

## 6. Publisher al broker

### 6.1 Mensaje (cerrado con Gian)

```csharp
// contracts/events/ReservationRequestedMessage.cs
// Contrato compartido con Booking. El correlation id se conserva en las propiedades del mensaje Service Bus.
public sealed record ReservationRequestedMessage(
    Guid RequestId,
    Guid EventId,
    Guid SeatId,
    Guid UserId,
    DateTimeOffset RequestedAt,
    long AmountCents,
    string Currency);
```

El tipo vive en el namespace `Booking.Business.Messages` y Infrastructure compila el archivo compartido desde `/contracts/events`. `RequestedAt` es `DateTimeOffset`; no hay un campo `correlationId` en el cuerpo. `AmountCents` y `Currency` se toman del evento asociado. `MessageId` y `CorrelationId` de Service Bus son el `RequestId` (BR-17, BR-19, BR-20).

### 6.2 Implementación

```csharp
public class ServiceBusReservationPublisher : IReservationPublisher
{
    // Se serializa en camelCase (BR-16): JsonSerializerDefaults.Web.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusSender _sender; // thread-safe: se reutiliza, no se crea por mensaje
    private readonly IEventRepository _events;
    private readonly ILogger<ServiceBusReservationPublisher> _logger;

    public ServiceBusReservationPublisher(ServiceBusSender sender, IEventRepository events, ILogger<ServiceBusReservationPublisher> logger)
    {
        _sender = sender;
        _events = events;
        _logger = logger;
    }

    public async Task PublishAsync(ReservationRequest request, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = request.Id });
        _logger.LogInformation("PublishStarted request {RequestId}", request.Id);

        var eventDetails = await _events.GetByIdAsync(request.EventId, ct)
            ?? throw new InvalidOperationException($"Event {request.EventId} was not found.");

        // requestId = Id; requestedAt = CreatedAt (BR-18); importe y moneda pertenecen al evento.
        var body = new ReservationRequestedMessage(
            request.Id, request.EventId, request.SeatId, request.UserId,
            new DateTimeOffset(DateTime.SpecifyKind(request.CreatedAt, DateTimeKind.Utc)),
            eventDetails.PriceCents, eventDetails.Currency);

        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(body, Json))
        {
            ContentType = "application/json",
            // MessageId y CorrelationId = requestId (BR-19, BR-20): un reintento del mismo
            // pedido siempre lleva el mismo identificador.
            MessageId = request.Id.ToString(),
            CorrelationId = request.Id.ToString()
        };

        try
        {
            await _sender.SendMessageAsync(message, ct);
            _logger.LogInformation("PublishCompleted request {RequestId}", request.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "PublishFailed request {RequestId}", request.Id);
            // BR-21: no se oculta el error. Application persiste PublishFailed y devuelve AppError.Unavailable (BR-21a).
            throw new ExternalServiceUnavailableException("No se pudo publicar la solicitud en Azure Service Bus.", ex);
        }
    }
}
```

`ExternalServiceUnavailableException` está definida en Application para respetar la dirección de dependencias de Clean Architecture.

### 6.3 Registro del cliente y del sender

Un solo `ServiceBusClient` y un solo `ServiceBusSender` para toda la aplicación (singleton). El contenedor de dependencias los libera al cerrar la app.

```csharp
services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    return new ServiceBusClient(o.ConnectionString, new ServiceBusClientOptions
    {
        // El SDK usa sus valores predeterminados; no se agrega una política paralela de Polly.
        RetryOptions = new ServiceBusRetryOptions()
    });
});
services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    return sp.GetRequiredService<ServiceBusClient>().CreateSender(o.TopicName);
});
services.AddScoped<IReservationPublisher, ServiceBusReservationPublisher>();
```

Si la connection string incluye `EntityPath=grupo5-booking-topic`, el nombre que se pasa a `CreateSender` debe coincidir con ese valor.

## 7. Cliente HTTP a Booking con token

### 7.1 Autenticación (confirmado)

- Flujo: `client_credentials` con el client `availability` del realm `seat-reservation`.
- El token que recibe tiene `aud` = `booking`; Booking lo valida. Availability no valida su propio token saliente.
- El token se cachea hasta poco antes de vencer (BR-30).
- En cada llamada se envía `Authorization: Bearer <token>` (BR-29).

```csharp
internal sealed class KeycloakTokenProvider
{
    private readonly IHttpClientFactory _factory;
    private readonly KeycloakOptions _options;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1); // evita pedir varios tokens a la vez
    private string? _token;
    private DateTimeOffset _expiresAt;

    public KeycloakTokenProvider(IHttpClientFactory factory, IOptions<KeycloakOptions> options, TimeProvider clock)
    {
        _factory = factory; _options = options.Value; _clock = clock;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (IsValid()) return _token!;

        await _gate.WaitAsync(ct);
        try
        {
            if (IsValid()) return _token!; // otro hilo ya lo renovó

            var http = _factory.CreateClient(HttpClientNames.Keycloak);
            using var response = await http.PostAsync(_options.TokenEndpoint, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret
                }), ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                       ?? throw new InvalidOperationException("Respuesta de token vacía.");
            _token = body.AccessToken;
            _expiresAt = _clock.GetUtcNow().AddSeconds(body.ExpiresIn);
            return _token;
        }
        finally { _gate.Release(); }
    }

    // Se renueva 30 segundos antes del vencimiento para no usar un token a punto de caducar.
    private bool IsValid() => _token is not null && _clock.GetUtcNow() < _expiresAt - TimeSpan.FromSeconds(30);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

internal sealed class BookingAuthHandler : DelegatingHandler
{
    private readonly KeycloakTokenProvider _tokens;
    public BookingAuthHandler(KeycloakTokenProvider tokens) => _tokens = tokens;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokens.GetTokenAsync(ct));
        return await base.SendAsync(request, ct);
    }
}
```

El `KeycloakTokenProvider` se registra como singleton, para que la caché sea una sola para toda la aplicación.

### 7.2 `BookingClient`

`BookingClient` implementa `IBookingClient` y se registra en `AddInfrastructure` para usar el `HttpClient` nombrado `Booking`, con los handlers de autenticación y correlación.

- `GetOccupiedSeatIdsAsync` consulta `GET api/events/{eventId}/occupied-seats`, deserializa los pares `seatId`/`isOccupied` y devuelve solo los identificadores ocupados. Una lista vacía en una respuesta exitosa devuelve un conjunto vacío.
- `GetStatusByRequestIdAsync` consulta `GET api/reservations/by-request/{requestId}` y envía el propio `requestId` en `X-Correlation-ID`. Un `404` devuelve `null`; una respuesta exitosa se deserializa a `BookingReservationStatusDto` (`status`, `description`).
- Errores HTTP distintos del `404` de estado, fallos de transporte, timeouts o respuestas inválidas se exponen como `ExternalServiceUnavailableException`. La cancelación solicitada por el llamador se propaga.

Un `404` en la consulta de estado se interpreta como `null` (BR-27): Booking todavía no tiene una reserva para esa solicitud, y Availability lo presenta como pendiente si existe localmente. Las llamadas a Booking usan `X-Correlation-ID`: `requestId` en la consulta de estado y el ID del middleware en consultas sin `requestId`.

## 8. Reintentos y timeouts

**Confirmado:** se quieren reintentos y timeouts en el cliente HTTP y en el publisher.

### 8.1 Cliente HTTP (propuesta técnica)

`Microsoft.Extensions.Http.Resilience` incluye `AddStandardResilienceHandler()`, un conjunto de políticas basado en Polly (timeout total, reintentos con espera creciente, timeout por intento y corte de circuito). **Acuerdo del grupo (2026-11-03):** se usan los valores predeterminados de la versión referenciada, sin personalizaciones. La política estándar considera respuestas transitorias; no reintenta `404`, que en la consulta de estado significa que la solicitud todavía no está procesada.

El orden de registro define el orden de la cadena de handlers: el primero registrado es el más externo. La resiliencia va primero para que **cada reintento tome el token vigente** del handler de autenticación.

```csharp
var bookingClient = services.AddHttpClient(HttpClientNames.Booking, (sp, client) =>
{
    client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<BookingClientOptions>>().Value.BaseUrl);
});
bookingClient.AddStandardResilienceHandler();     // valores predeterminados; registrado antes de los handlers delegantes
bookingClient.AddHttpMessageHandler<CorrelationIdHandler>();
bookingClient.AddHttpMessageHandler<BookingAuthHandler>();

// El pedido de token a Keycloak también lleva la política estándar predeterminada.
services.AddHttpClient(HttpClientNames.Keycloak).AddStandardResilienceHandler();
```

La resiliencia HTTP se instala antes de los handlers delegantes para que cada reintento vuelva a pasar por autenticación y correlación. Los `GET` a Booking son seguros para reintentar; el `404` no es transitorio y no se reintenta.

### 8.2 Publisher

**Acuerdo del grupo (2026-11-03):** el envío usa exclusivamente los reintentos nativos del SDK de Azure Service Bus mediante `ServiceBusRetryOptions` con sus valores predeterminados; no se agrega Polly ni otra capa de reintentos.

**Consecuencia a tener presente.** Un reintento puede producir un mensaje duplicado: el envío llegó al broker, pero la confirmación se perdió. Se cubre con el `MessageId` y `CorrelationId` iguales al `requestId` (BR-19, BR-20) y con la idempotencia de Booking por `requestId`.

## 9. Logs de auditoría

**Convención acordada (2026-11-03):**

- El id de la solicitud (`requestId`) es el `correlationId` en los tres servicios y aparece en Availability, en el mensaje del broker y en Booking (BR-19, BR-41).
- Cada operación registra inicio, resultado y fin con `Started`, `Completed` y `Failed`.
- Serilog es el proveedor acordado; se deben emitir logs estructurados a consola con `Service`, `Operation`, `Event` y `CorrelationId`. El formato actual de la API aún no muestra todas las propiedades estructuradas y requiere integración de Luciana.
- Se agregan duración, status HTTP y excepción cuando corresponda. Nunca se registran secretos, tokens, connection strings ni payloads sensibles.

Los componentes usan `ILogger<T>` y scopes para incluir la correlación en los logs de la operación:

```csharp
using var scope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = requestId });
_logger.LogInformation("{Operation} Started", operation);
// ... trabajo ...
_logger.LogInformation("{Operation} Completed", operation);   // o LogError(..., "{Operation} Failed") en el catch
```

| Operación | Dónde se registra | Correlación |
|---|---|---|
| Publicación del mensaje | `ServiceBusReservationPublisher` | `requestId` |
| Consulta del estado a Booking | `BookingClient` | `requestId` |
| Consulta de ocupación a Booking | `BookingClient` | ID recibido o generado por el middleware HTTP |
| Guardar o actualizar la solicitud | `ReservationRequestRepository` | `requestId` |

Nunca se registran secretos, tokens ni la connection string.

## 10. Manejo de errores

| Situación | Comportamiento de Infrastructure | Regla |
|---|---|---|
| Falla el envío al broker | Lanza `ExternalServiceUnavailableException` | BR-21, BR-28 |
| El publisher lanza `ExternalServiceUnavailableException` | Application persiste `PublishFailed` y devuelve `AppError.Unavailable` | Confirmado por Feli (2026-11-02) |
| Booking responde `404` en el estado | Devuelve `null` | BR-27 |
| Booking no responde o responde con error | Lanza `ExternalServiceUnavailableException` | BR-28 |
| Solicitud inexistente en `GetByIdAsync` | Devuelve `null` | BR-13 |
| Solicitud inexistente en `UpdateStatusAsync` | Lanza `KeyNotFoundException` | BR-11 |

La excepción compartida para fallas de servicios externos está definida en Application:

```csharp
namespace Availability.Application.Common;

public class ExternalServiceUnavailableException : Exception
{
    public ExternalServiceUnavailableException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
```

Infrastructure lanza esta excepción y Application puede capturarla sin conocer los detalles de Azure Service Bus ni de Booking.

## 11. Registro de dependencias

```csharp
// Availability.Infrastructure/DependencyInjection.cs
public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<ServiceBusOptions>(configuration.GetSection("ServiceBus"));
    services.Configure<BookingClientOptions>(configuration.GetSection("Booking"));
    services.Configure<KeycloakOptions>(configuration.GetSection("Keycloak"));

    services.AddDbContext<AvailabilityDbContext>(o =>
        o.UseNpgsql(configuration.GetConnectionString("AvailabilityDb")));

    services.AddScoped<IReservationRequestRepository, ReservationRequestRepository>();
    services.AddScoped<IEventRepository, EventRepository>();
    services.AddScoped<ISeatRepository, SeatRepository>();

    // ... ServiceBusClient, ServiceBusSender y publisher (sección 6.3)
    // ... KeycloakTokenProvider (singleton), BookingAuthHandler, BookingClient y resiliencia (sección 8)

    services.AddSingleton(TimeProvider.System);
    return services;
}
```

Luciana lo invoca desde `Program.cs` del Api con `AddInfrastructure(builder.Configuration)`.

## 12. Pruebas

El framework de pruebas y la estrategia para los repositorios (PostgreSQL real o base en memoria) están **pendientes**. Una base en memoria no reproduce algunas restricciones de PostgreSQL (claves únicas, conversiones y tipos de fecha), así que la decisión conviene tomarla con el grupo.

Qué debe verificarse, independiente del framework, y a qué criterio de aceptación responde:

| Prueba | Criterio |
|---|---|
| `SaveAsync` con `Id` repetido falla | Clave primaria (BR-08) |
| `UpdateStatusAsync` cambia el estado y los tres valores del enum se guardan y se leen | BR-10, BR-11 |
| `ExistsAsync` es `false` si el asiento no pertenece al evento | BR-01 |
| `GetByUserIdAsync` devuelve solo las solicitudes del usuario | BR-14 |
| El seed carga al menos un evento con asientos y es reproducible | BR-04 |
| El publisher envía el JSON acordado con `MessageId` y `CorrelationId` iguales al `requestId` | BR-17, BR-19 |
| El cliente devuelve los `seatId` con `isOccupied` en `true`, y vacío si Booking responde `200` con lista vacía | BR-25, BR-26 |
| `GetStatusByRequestIdAsync` devuelve `null` con `404` y lanza `ExternalServiceUnavailableException` ante un error | BR-27, BR-28 |
| El token se pide una sola vez mientras es válido y se envía en cada llamada | BR-29, BR-30 |

Para el publisher y el cliente HTTP no hace falta ni el broker ni Booking: el cliente se prueba con un `HttpMessageHandler` falso, y el publisher contra una abstracción del sender o con un consumidor de prueba sobre la suscripción `booking`.

## 13. Pendientes y puntos por confirmar

### Datos de infraestructura

| Pendiente | Con quién |
|---|---|
| Servicio y versión de PostgreSQL en el compose; nombre del host y credenciales para la connection string | Felipe |
| Dirección de Booking en el compose y nombre de la variable | Felipe / Santi |
| Nombre de la variable de entorno del secreto de Availability en su contenedor | Felipe |
| Valor del token endpoint dentro de la red del compose y efecto sobre el emisor (`iss`) | Felipe |
| `depends_on` con healthcheck para que Availability espere a PostgreSQL | Felipe |
| Convención de logs más allá del `correlationId` (formato, proveedor, campos) | Felipe |

### Decisiones del grupo

| Pendiente | Con quién |
|---|---|
| Framework de pruebas y estrategia para repositorios | Grupo |
| Datos del seed: cuántos eventos y asientos, y sus GUID | Rebeca |
| Qué `correlationId` usar en consultas sin `requestId` (listado de asientos y ocupación) | Felipe / Feli |

### Reglas pendientes (de `business-rules.md`)

| Regla | Con quién |
|---|---|
| BR-P-04 Qué hace Application si Booking no responde | Feli |
| BR-P-05 Ubicación de `ReservationRequestedMessage` | Feli |
| BR-P-06 Propiedades extra en el sobre del mensaje | Gian |
| BR-P-07 `requestedAt` como `DateTimeOffset` en el consumidor | Gian |

## 14. Orden sugerido de implementación

1. Proyecto Infrastructure con referencias y paquetes; clases de opciones.
2. `AvailabilityDbContext`, configuraciones y migración inicial; seed.
3. Repositorios y sus pruebas.
4. `DatabaseInitializer` y `AddInfrastructure`; verificar que la app migra al iniciar.
5. Publisher con un consumidor de prueba sobre la suscripción `booking`.
6. Token provider y handler de autenticación, probados contra Keycloak.
7. `BookingClient` con un handler falso; luego contra Booking real.
8. Resiliencia y logs de auditoría.
9. Comentarios de decisiones y patrones en el código, e historial de prompts en `/prompts`.
