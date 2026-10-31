using Booking.Api.Errors;
using Booking.Api.Security;
using Booking.Business.Queries;
using Booking.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier));
builder.Services.AddExceptionHandler<BookingExceptionHandler>();
builder.Services.AddBookingAuthentication(builder.Configuration, builder.Environment);

// Composition root: aquí se registrará IReservationRepository con su implementación
// EF. La factory permite arrancar /health mientras falta esa integración y devuelve
// un 503 explícito al consultar, sin introducir un repositorio ficticio en la API.
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
