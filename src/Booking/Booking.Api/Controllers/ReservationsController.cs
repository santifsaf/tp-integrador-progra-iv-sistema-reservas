using Booking.Api.Security;
using Booking.Business.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

// N-Layer: el controlador adapta HTTP y delega la consulta en Business;
// el acceso a datos queda detrás del repositorio utilizado por ese servicio.
[ApiController]
[Route("api/reservations")]
[Authorize(Policy = BookingAuthentication.AvailabilityPolicy)]
public sealed class ReservationsController(ReservationStatusQueryService queries) : ControllerBase
{
    [HttpGet("by-request/{requestId}")]
    [ProducesResponseType<ReservationStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationStatusDto>> GetByRequestId(
        Guid requestId, CancellationToken ct)
    {
        // Sin la restricción :guid, [ApiController] devuelve 400 si el formato es
        // inválido. Guid.Empty también se rechaza: no identifica una solicitud real.
        if (requestId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(requestId), "El identificador no puede estar vacío.");
            return ValidationProblem(ModelState);
        }

        var result = await queries.GetByRequestIdAsync(requestId, ct);
        if (result is null)
        {
            // Availability decide si muestra pendiente usando su registro local.
            // Booking no inventa PENDING como estado de reserva.
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Reserva no encontrada",
                detail: "Booking todavía no tiene una reserva para esa solicitud.");
        }

        return Ok(result);
    }
}
