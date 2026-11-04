# Prompt 1:

Quiero que generes el código para un DbContext y la configuración de una entidad llamada 'Reservation' siguiendo estas especificaciones:
Usá 'File-scoped namespaces' para Booking.Data.Persistence. El DbContext debe llamarse 'BookingDbContext', ser una clase 'public sealed' y utilizar la sintaxis de 'Primary Constructor' para recibir 'DbContextOptions<BookingDbContext> options'. Exponé la entidad usando 'public DbSet<Reservation> Reservations => Set<Reservation>();'. Sobreescribí 'OnModelCreating' para aplicar la configuración de forma limpia usando 'modelBuilder.ApplyConfiguration(new ReservationConfiguration());'.
Para ReservationConfiguration: Debe implementar 'IEntityTypeConfiguration<Reservation>'. Mapeá la entidad a una tabla llamada "reservations" en minúsculas. Definí constantes públicas para los nombres de los índices: -ActiveSeatIndex = "ux_reservations_active_seat" -RequestIndex = "ux_reservations_request" -ExpirationIndex = "ix_reservations_expiration"
Mapeo de Propiedades (todo en snake_case para la base de datos):
•	Clave primaria: Id (mapeado como "id", con .ValueGeneratedNever()).
•	Propiedades básicas: RequestId ("request_id"), UserId ("user_id"), EventId ("event_id"), SeatId ("seat_id").
•	Enums como Texto: Las propiedades 'Status' (max 20 caracteres) y 'RejectionReason' (max 30 caracteres) deben convertirse a string en la base de datos (.HasConversion<string>()).
•	Auditoría: CreatedAt ("created_at"), ExpiresAt ("expires_at"), UpdatedAt ("updated_at").
Para concurrencia, crear un índice único compuesto por (EventId, SeatId) llamado 'ux_reservations_active_seat'. Debe tener un filtro condicional (.HasFilter) para que solo aplique cuando el status sea 'Held' o 'Confirmed'. Para idempotencia, crear un índice único para 'RequestId' llamado 'ux_reservations_request'. Para buscar rápido las Held vencidas, creá un índice no único para 'ExpiresAt' llamado 'ix_reservations_expiration' con un filtro condicional para cuando el status sea 'Held'.
Por favor dejame revisar el código antes de implementar los cambios.


# Prompt 2:
Para esta capa de datos de Booking que estoy desarrollando, que es un microservicio N-Layer en .NET 10 con EF Core 10 y Npgsql sobre PostgreSQL. Dominio anémico: la lógica de negocio vive en Booking.Business, no en esta capa. Usá de contexto: IReservationRepository.cs, Reservation.cs, ReservationConfiguration.cs y BookingDbContext.cs.
Escribí la clase ReservationRepository (public sealed, file-scoped namespace Booking.Data.Persistence, primary constructor que recibe BookingDbContext, implementa IReservationRepository) con SOLO dos métodos por ahora: GetByRequestIdAsync y TryHoldAsync. Los demás métodos de la interfaz deben lanzar NotImplementedException con un comentario TODO, solo para que compile.
Comportamiento de GetByRequestIdAsync:
•	Devuelve la reserva de esa solicitud o null si no existe.
•	Solo lectura: sin tracking de entidades.
Comportamiento de TryHoldAsync:
•	Inserta la reserva (ya viene en estado Held) y guarda los cambios.
•	Si inserta bien, devuelve HoldResult.Created.
•	Si Postgres rechaza el insert por violación de unicidad (SqlState 23505, vía DbUpdateException con PostgresException interna), NO debe lanzar excepción en los dos casos de conflicto previstos:
•	Si ya existe una reserva con ese RequestId, devuelve DuplicateRequest. Esto se decide consultando la base, no mirando qué índice reportó Postgres, porque un reintento puede violar los dos índices a la vez y no está garantizado cuál informa primero.
•	Si no, y ConstraintName coincide con ReservationConfiguration.ActiveSeatIndex, devuelve SeatOccupied.
•	Tras el fallo, la entidad debe quedar descartada del contexto (Detached), porque el llamador reutiliza el mismo objeto en AddRejectedAsync y, si queda trackeada, rompe el siguiente insert.
•	Cualquier otro error (otra violación de unicidad u otra excepción) se propaga sin tragarlo.
Restricciones:
•	Todo asincrónico y con CancellationToken propagado.
•	Sin lógica de negocio, sin leer el reloj, sin logging, sin abrir transacciones propias ni reintentos.
•	Usá las constantes de ReservationConfiguration, no strings sueltos.
•	Comentarios en español explicando las decisiones no obvias (por qué se hace el chequeo de duplicado por consulta, por qué se descarta la entidad).
•	No toques las demás clases ni agregues paquetes.
Formato de respuesta: Devolveme solo el archivo ReservationRepository.cs completo, y después una lista breve de las decisiones que tomaste y de los casos límite que el código NO cubre.

# Prompt 3:
Estoy armando los tests de integración de la capa de datos de Booking (.NET 10, EF Core 10, Npgsql, PostgreSQL, xUnit 2.9.3). Ya existe el proyecto Booking.Data.Tests, que referencia a Booking.Data, y una base de pruebas llamada booking_tests en un Postgres local de Docker (usuario booking, contraseña booking, puerto 5432). Podés leer Booking.Data.Tests.csproj, ReservationRepository.cs, IReservationRepository.cs, Reservation.cs y BookingDbContext.cs como contexto; podés usar ReservationRequestProcessorTests.cs como referencia de estilo.
Generá dos archivos en el namespace Booking.Data.Tests.
Archivo 1: PostgresFixture.cs
•	Clase sealed que implementa IAsyncLifetime con la firma de xUnit 2.9.3 (Task, no ValueTask).
•	Lee la connection string de la variable de entorno BOOKING_TEST_DB y, si no existe, usa la local por defecto de booking_tests.
•	Expone CreateContext(), que devuelve un BookingDbContext NUEVO en cada llamada (DbContext no es thread-safe y los futuros tests de concurrencia necesitan uno por tarea).
•	En InitializeAsync, antes de hacer nada, verifica que el nombre de la base termine en "_tests"; si no, lanza InvalidOperationException. Después aplica las migraciones con MigrateAsync (así el test también valida la migración).
•	Expone ResetAsync(), que vacía la tabla reservations con TRUNCATE.
•	Incluye en el mismo archivo la CollectionDefinition "Postgres" con ICollectionFixture<PostgresFixture>, para que las clases de test corran en serie.
Archivo 2: ReservationRepositoryHoldTests.cs
•	Clase marcada con [Collection("Postgres")] y [Trait("Category", "Integration")], que recibe la fixture por constructor y vacía la tabla antes de cada test.
•	Un helper para construir reservas Held válidas (con RequestId, SeatId y fechas configurables) y otro que ejecuta TryHoldAsync con un contexto propio.
•	Tests, con nombres en español que describan el comportamiento:
1.	GetByRequestIdAsync sin reserva existente devuelve null.
2.	TryHoldAsync con asiento libre devuelve Created y la reserva queda guardada en estado Held (verificalo leyendo con un contexto distinto).
3.	TryHoldAsync dos veces con el mismo RequestId (otro Id de reserva) devuelve DuplicateRequest y no lanza.
4.	TryHoldAsync con otro RequestId sobre el mismo asiento devuelve SeatOccupied y no lanza.
5.	Después de un SeatOccupied, el MISMO contexto y repositorio permiten insertar una reserva en otro asiento (devuelve Created). Esto prueba que la entidad fallida se descartó del contexto.
Restricciones:
•	Solo xUnit con Assert; sin librerías de mocking ni de aserciones nuevas.
•	No uses la base de desarrollo ni agregues paquetes.
•	Seguí el estilo y las convenciones del archivo de referencia que te adjunto.
•	Comentá en español solo las decisiones no obvias (base separada, seguro anti-desastres, un contexto por operación).
Formato de respuesta: Los dos archivos completos, y después una lista breve de los supuestos que hiciste.

# Prompt 3 correcciones
RESTART IDENTITY CASCADE sobra Database puede ser null si alguien define mal BOOKING_TEST_DB, y eso daría un NullReferenceException poco claro. Con connectionBuilder.Database ?? "" el error sería el mensaje que escribiste En los métodos asincronos usá await using en lugar de using. Override no tiene clase base: corregilo, porque no tiene nada que sobreescribir Además BuildHeldReservation genera un EventId y un SeatId nuevos en cada llamada. En ese test, las dos reservas tienen el mismo RequestId pero distinto evento y asiento, así que el segundo insert viola un solo índice (ux_reservations_request). Pasaría igual aunque el repositorio usara solo el nombre del índice. Debería agregarse eventID como parámetro del helper

# Prompt 4:
Para esta nueva tarea podés usar de contexto ReservationRepository.cs, IReservationRepository.cs, Reservation.cs, BookingDbContext.cs, ReservationConfiguration.cs, PostgresFixture.cs y ReservationRepositoryHoldTests.cs (como referencia de estilo). Sigo con la capa de datos de Booking (.NET 10, EF Core 10, Npgsql, PostgreSQL, xUnit 2.9.3). Ya tengo implementados y probados GetByRequestIdAsync y TryHoldAsync.
Implementá AddRejectedAsync y TryResolveHeldAsync en ReservationRepository, y escribí sus tests de integración contra Postgres real.
Cambio previo en el repositorio:
•	Extraé un método privado auxiliar que, dado un DbUpdateException, indique si es una violación de unicidad de Postgres y devuelva el nombre del constraint. Usá PostgresErrorCodes.UniqueViolation de Npgsql en lugar de un string suelto.
•	TryHoldAsync debe usarlo sin cambiar su comportamiento actual (sus 5 tests existentes tienen que seguir pasando).
Comportamiento de AddRejectedAsync:
•	Recibe una reserva que ya viene en estado Rejected. Si su Status no es Rejected, lanza ArgumentException (evita saltear TryHoldAsync y su control de concurrencia).
•	La inserta y devuelve true.
•	Si falla por violación de unicidad sobre el índice de RequestId (ReservationConfiguration.RequestIndex), significa que esa solicitud ya tenía reserva: devuelve false, sin lanzar.
•	En ese caso de fallo, la entidad debe quedar descartada del contexto (Detached).
•	Cualquier otra violación de unicidad o cualquier otra excepción se propaga.
•	Contexto real de uso: la capa de negocio llama primero a TryHoldAsync con un objeto Reservation, recibe SeatOccupied, muta ese mismo objeto a Rejected/SeatOccupied y se lo pasa a AddRejectedAsync usando el mismo contexto y repositorio. Eso tiene que funcionar.
Comportamiento de TryResolveHeldAsync(reservationId, to, reason, now, ct):
•	Solo acepta como destino Confirmed o Rejected; cualquier otro lanza ArgumentOutOfRangeException.
•	Si el destino es Rejected, reason es obligatorio (ArgumentException si es null). Si el destino es Confirmed, RejectionReason se guarda en null aunque llegue un valor.
•	Tiene que ser UNA sola sentencia atómica en la base (ExecuteUpdateAsync), con estas condiciones en el WHERE: Id coincide, Status es Held y ExpiresAt es estrictamente mayor que 'now'. No se lee la reserva antes ni se chequea el plazo en C#.
•	Actualiza Status, RejectionReason y UpdatedAt (= now).
•	Devuelve true solo si afectó exactamente una fila; false significa "ya no estaba vigente" y NO es un error.
•	No usa el change tracker (ExecuteUpdate lo saltea), así que no deben quedar entidades trackeadas.
Tests (xUnit, Assert, mismo estilo y fixture que el archivo de referencia; cada test empieza vaciando la tabla): Creá TestData.cs (clase estática interna) con helpers para construir reservas Held y Rejected, para no duplicar código entre archivos. No modifiques el archivo de tests existente. Usá una fecha 'now' FIJA en los tests de plazo (no UtcNow), para que los límites sean deterministas. Sembrá reservas en estados específicos insertándolas directo con el DbContext, no a través del repositorio.
Archivo ReservationRepositoryRejectedTests.cs:
1.	Tras un SeatOccupied, mutar la misma entidad a Rejected/SeatOccupied y llamar a AddRejectedAsync con el MISMO contexto y repositorio: devuelve true y la fila queda guardada como Rejected con motivo SeatOccupied (verificalo con otro contexto).
2.	Llamarlo dos veces con el mismo RequestId: la segunda devuelve false, no lanza y hay una sola fila para esa solicitud.
3.	Una reserva Rejected no libera ni ocupa el asiento: el ocupante Held original sigue ahí y otra solicitud sobre ese asiento sigue dando SeatOccupied.
4.	Pasar una reserva con Status distinto de Rejected lanza ArgumentException.
Archivo ReservationRepositoryResolveTests.cs:
1.	Held vigente a Confirmed: true; en la base Status Confirmed, RejectionReason null y UpdatedAt igual a 'now'.
2.	Held vigente a Rejected con PaymentFailed: true; en la base queda el motivo.
3.	Held con ExpiresAt anterior a 'now': false y sigue Held.
4.	Held con ExpiresAt exactamente igual a 'now': false (el límite es estricto).
5.	Reserva ya Confirmed: resolver otra vez a Rejected devuelve false y NO cambia el estado.
6.	Id inexistente: false.
7.	Destino inválido (Held o Expired): ArgumentOutOfRangeException. Rejected sin motivo: ArgumentException.
8.	Carrera: 10 tareas en paralelo, cada una con su propio contexto, resuelven la MISMA reserva Held vigente (mitad a Confirmed, mitad a Rejected). Exactamente una devuelve true.
9.	Pasar a Rejected libera el asiento (otra solicitud sobre ese asiento ahora da Created); pasar a Confirmed lo mantiene ocupado (da SeatOccupied).
Restricciones:
•	Sin librerías nuevas ni mocking. No toques las demás clases, la fixture ni las migraciones. ExpireHeldAsync sigue como NotImplementedException.
•	Sin lógica de negocio, sin leer el reloj dentro del repositorio (el 'now' llega por parámetro), sin logging ni reintentos.
•	Comentarios en español solo para decisiones no obvias.
Formato de respuesta:
1.	ReservationRepository.cs completo, 2) los tres archivos de tests (TestData.cs y los dos de arriba), 3) una lista breve de supuestos y de casos que el código NO cubre.


# Prompt 4 correcciones:
Faltó using Microsoft.EntityFrameworkCore en RejectedTests y ResolveTests. TryHoldAsync corre en un contexto que se descarta, mientras que AddRejectedAsync corre en otro nuevo. Así nunca se verifica que, tras el SeatOccupied, el mismo contexto pueda reinsertar el objeto mutado. Reescribilo con un solo contexto y repositorio. Bug en el test de la carrera. En un for, la variable i es una sola y se comparte entre las tareas. Como se ejecutan en el thread pool, varias pueden ver i == 10, y entonces el reparto mitad Confirmed y mitad Rejected no está garantizado. Además, las tareas no arrancan a la vez, así que la contención es débil. Usá un portón de salida y Enumerable.Range. Contextos sin liberar: fixture.CreateContext() se pasa sin await using en el segundo test de Rejected y en PassAsRejected y PassAsConfirmed. Declaralos con await using var en una línea aparte. Además: En el test 3 de Rejected, asertá que AddRejectedAsync devuelve true; hoy se ignora su resultado. En el test 2, ToListAsync() y filtrar en memoria es innecesario: CountAsync(r => r.RequestId == requestId) y Assert.Equal(1, ...). Sumale Assert.Empty(ctx.ChangeTracker.Entries()) para cubrir el descarte en el caso de fallo. Los mensajes de excepción (Contains("debe estar en estado Rejected")) atan el test al texto exacto. Es más robusto asertar ex.ParamName ("reservation" o "reason"). PassAsRejected... mezcla idiomas: renombralo a PasarARejected_LiberaElAsiento y PasarAConfirmed_MantieneLaOcupacion. DestinoInvalido solo prueba Held. Un [Theory] con Held y Expired cubre lo que pedía el prompt.


# Prompt 5:

Contexto: Sigo con la capa de datos de Booking (.NET 10, EF Core 10, Npgsql, PostgreSQL, xUnit 2.9.3). Tengo implementados y probados los demás métodos de ReservationRepository. Falta GetOccupiedSeatIdsAsync(Guid eventId, CancellationToken ct), que hoy lanza NotImplementedException. Lo consume la API de consulta, que cruza esos ids con el catálogo de asientos del servicio Availability. Podés consultar ReservationRepository.cs, IReservationRepository.cs, Reservation.cs, ReservationConfiguration.cs, TestData.cs y ReservationRepositoryExpireTests.cs (como referencia de estilo).
Tarea: Implementá GetOccupiedSeatIdsAsync en ReservationRepository y escribí sus tests de integración.
Comportamiento:
•	Devuelve los SeatId DISTINTOS de las reservas de ese evento cuyo Status es Held o Confirmed. El filtro de estado tiene que ser exactamente ese, porque coincide con el predicado del índice parcial ux_reservations_active_seat y así Postgres puede usarlo.
•	Si no hay ninguna, devuelve una lista vacía (nunca null).
•	Solo lectura, sin tracking, y proyectando solo SeatId (no traer la entidad completa).
•	No filtra por ExpiresAt: una Held vencida que el job todavía no procesó sigue contando como ocupada. Es una ventana de consistencia eventual aceptada; dejalo comentado.
•	No lee el reloj, sin lógica de negocio, sin logging.
Tests (xUnit, Assert, mismo estilo y fixture que el archivo de referencia; cada test empieza vaciando la tabla). Archivo ReservationRepositoryOccupiedSeatsTests.cs, con su propio helper SeedAsync que siembra con un contexto aparte y descartable:
1.	Evento sin reservas: lista vacía.
2.	Held y Confirmed del evento: aparecen sus asientos.
3.	Rejected y Expired del evento: no aparecen.
4.	Reservas Held de OTRO evento: no aparecen.
5.	Un asiento con una Rejected y una Held: aparece una sola vez.
6.	Held vencida (ExpiresAt en el pasado) que todavía no fue procesada: sigue apareciendo.
Restricciones:
•	Sin librerías nuevas ni mocking. No toques la fixture, las migraciones, TestData.cs ni los otros archivos de tests.
•	Comentarios en español solo para decisiones no obvias.
Formato de respuesta:
1.	ReservationRepository.cs completo, 2) ReservationRepositoryOccupiedSeatsTests.cs, 3) una lista breve de supuestos y de casos que el código NO cubre.




# Prompt 6:
Contexto: Agregué a BookingDataServiceCollectionExtensions el método de extensión MigrateBookingDatabaseAsync(this IServiceProvider services, CancellationToken ct = default): crea un scope, obtiene BookingDbContext con GetService (puede ser null si no se registró AddBookingData) y, si existe, llama a Database.MigrateAsync. Si no existe, no hace nada. Podés leer como contexto BookingDataServiceCollectionExtensions.cs, PostgresFixture.cs, BookingDbContext.cs y ReservationRepositoryOccupiedSeatsTests.cs como referencia de estilo. Tarea: Escribí los tests de integración de ese método en un archivo nuevo, BookingDatabaseMigrationTests.cs, namespace Booking.Data.Tests. Ya está expuesto en PostgresFixture una propiedad pública ConnectionString que devuelve la cadena de conexión que la fixture ya usa (el campo _connectionString). Tests (xUnit, Assert, mismo estilo que el archivo de referencia; clase con [Collection("Postgres")] y [Trait("Category", "Integration")], fixture por constructor):
1.	Sin AddBookingData: un ServiceCollection vacío, construido con BuildServiceProvider, llama a MigrateBookingDatabaseAsync y termina sin lanzar excepción (representa el entorno Testing, sin connection string).
2.	Con AddBookingData(_fixture.ConnectionString): construir el proveedor y llamar a MigrateBookingDatabaseAsync DOS veces seguidas sin que falle (idempotencia). Después, con un scope nuevo, comprobar que no hay migraciones pendientes (Database.GetPendingMigrationsAsync vacío) y que las migraciones aplicadas (GetAppliedMigrationsAsync) coinciden con todas las del ensamblado (Database.GetMigrations()). Restricciones: •	La base booking_tests la comparten los demás tests: no borres ni recrees el esquema, no hagas TRUNCATE y no uses EnsureDeleted. Los tests solo leen el estado de las migraciones. •	Liberá el proveedor y los scopes con await using. •	Sin librerías nuevas ni mocking. No toques las migraciones, el DbContext ni los otros archivos de tests. •	Comentarios en español solo para decisiones no obvias. Formato de respuesta: BookingDatabaseMigrationTests.cs y una lista breve de supuestos y de lo que estos tests NO cubren.