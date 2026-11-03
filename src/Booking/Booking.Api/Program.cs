using Booking.Api.Errors;
using Booking.Api.Payments;
using Booking.Api.Security;
using Booking.Business.Queries;
using Booking.Data;
using Booking.Data.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier));
builder.Services.AddExceptionHandler<BookingExceptionHandler>();
builder.Services.AddBookingAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddPaymentCommunication(builder.Configuration);


var bookingConnectionString = builder.Configuration.GetConnectionString("Booking");
if (!string.IsNullOrWhiteSpace(bookingConnectionString))
{
    builder.Services.AddBookingData(bookingConnectionString);
}
else if (!builder.Environment.IsEnvironment("Testing"))
{
    // Fallar al arrancar es mejor que responder 503 en producción sin saber por qué.
    throw new InvalidOperationException("Falta la connection string 'ConnectionStrings:Booking'.");
}

// Composition root: IReservationRepository se registra arriba con AddBookingData cuando hay
// connection string. Estas factories se mantienen como red de seguridad: si el repositorio no
// está registrado (por ejemplo en las pruebas de la API, entorno Testing), /health igual
// arranca y las consultas devuelven un 503 explícito, sin introducir un repositorio ficticio.

builder.Services.AddScoped(sp => new ReservationStatusQueryService(
    sp.GetService<IReservationRepository>() ?? throw new ReservationRepositoryNotConfiguredException(),
    sp.GetRequiredService<ILogger<ReservationStatusQueryService>>()));
builder.Services.AddScoped(sp => new OccupiedSeatsQueryService(
    sp.GetService<IReservationRepository>() ?? throw new ReservationRepositoryNotConfiguredException(),
    sp.GetRequiredService<ILogger<OccupiedSeatsQueryService>>()));

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
    await Results.Problem(statusCode: context.HttpContext.Response.StatusCode)
        .ExecuteAsync(context.HttpContext));
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { service = "Booking", status = "ok" }));
app.Run();

// Hace accesible el punto de entrada al host HTTP de las pruebas de integración.
public partial class Program;
