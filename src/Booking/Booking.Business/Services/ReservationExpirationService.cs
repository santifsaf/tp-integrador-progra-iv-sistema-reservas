using Booking.Data;
using Microsoft.Extensions.Logging;
using static Booking.Business.Auditing.AuditLog;

namespace Booking.Business.Services;

// Paso 8 del flujo: una reserva Held que no llega a un resultado de pago dentro de su
// plazo pasa a Expired y el asiento queda libre.
//
// DECISIÓN: el vencimiento se hace con un solo UPDATE condicional en la base
// (WHERE status = 'Held' AND expires_at <= now). Por eso:
//   - No importa si varias réplicas de Booking corren el job a la vez: cada fila la
//     actualiza una sola réplica, y las otras no encuentran nada.
//   - No pisa una confirmación que llegó justo antes: esa fila ya no está Held.
// El asiento se libera solo, porque el índice de ocupación filtra por estado.
public sealed class ReservationExpirationService(
    IReservationRepository repository,
    TimeProvider clock,
    ILogger<ReservationExpirationService> logger)
{
    public async Task<int> ExpireHeldAsync(CancellationToken ct)
    {
        var expired = await repository.ExpireHeldAsync(clock.GetUtcNow(), ct);

        foreach (var reservation in expired)
        {
            // El vencimiento cierra la cadena de sucesos de esa solicitud.
            using var _ = BeginChain(logger, reservation.RequestId);
            End(logger, reservation.RequestId, $"ReservationExpired {reservation.Id} (seat released)");
        }

        return expired.Count;
    }
}
