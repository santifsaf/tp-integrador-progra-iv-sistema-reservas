using Booking.Api.Security;
using Booking.Business.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

// N-Layer: la API valida la entrada y adapta la respuesta HTTP;
// Business y Data resuelven la consulta sin acceder a la base de Availability.
[ApiController]
[Route("api/events")]
[Authorize(Policy = BookingAuthentication.AvailabilityPolicy)]
public sealed class EventsController(OccupiedSeatsQueryService queries) : ControllerBase
{
    [HttpGet("{eventId}/occupied-seats")]
    [ProducesResponseType<Guid[]>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<Guid>>> GetOccupiedSeats(Guid eventId, CancellationToken ct)
    {
        if (eventId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(eventId), "El identificador no puede estar vacío.");
            return ValidationProblem(ModelState);
        }

        // Booking informa su ocupación: [] también es válido si no conoce el evento.
        // La existencia del evento se valida en Availability, que tiene el catálogo.
        return Ok(await queries.GetByEventIdAsync(eventId, ct));
    }
}
