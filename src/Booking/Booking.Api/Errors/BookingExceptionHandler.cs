using Microsoft.AspNetCore.Diagnostics;

namespace Booking.Api.Errors;

internal sealed class BookingExceptionHandler(ILogger<BookingExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            return false;

        // El middleware de excepciones limpia la ruta actual, pero conserva los
        // valores originales en esta feature para mantener la correlación del log.
        var feature = httpContext.Features.Get<IExceptionHandlerFeature>();
        object? requestId = null;
        object? eventId = null;
        if (feature?.RouteValues is { } routeValues)
        {
            routeValues.TryGetValue("requestId", out requestId);
            routeValues.TryGetValue("eventId", out eventId);
        }

        logger.LogError(exception, "Fallo de Booking. RequestId={RequestId} EventId={EventId} TraceId={TraceId}",
            requestId, eventId, httpContext.TraceIdentifier);

        var unavailable = exception is ReservationRepositoryNotConfiguredException;
        // El detalle técnico queda en logs. El cliente recibe ProblemDetails sin
        // stack traces, cadenas de conexión ni mensajes internos de excepciones.
        await Results.Problem(
            statusCode: unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError,
            title: unavailable ? "Consulta temporalmente no disponible" : "Error al consultar Booking",
            detail: "No se pudo completar la operación. Intentá nuevamente más tarde.")
            .ExecuteAsync(httpContext);
        return true;
    }
}
