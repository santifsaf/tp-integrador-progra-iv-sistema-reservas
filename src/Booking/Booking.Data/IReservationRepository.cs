using Booking.Data.Entities;

namespace Booking.Data;

// CONTRATO ENTRE LA CAPA DE NEGOCIO (Gian) Y LA CAPA DE DATOS (Luis).
//
// DECISIÓN: el control de concurrencia lo hace la BASE, no el código.
// Booking corre con varias réplicas consumiendo la misma suscripción, así que dos
// réplicas pueden procesar a la vez solicitudes distintas para el mismo asiento.
// Un "if (asiento libre) insertar" en C# tiene una carrera entre la consulta y el insert.
// Con un índice único parcial, Postgres garantiza que solo un insert gane:
//
//   -- Concurrencia: un solo Held/Confirmed por asiento.
//   CREATE UNIQUE INDEX ux_reservations_active_seat
//       ON reservations (event_id, seat_id)
//       WHERE status IN ('Held', 'Confirmed');
//
//   -- Idempotencia: una solicitud genera como máximo una reserva.
//   CREATE UNIQUE INDEX ux_reservations_request ON reservations (request_id);
//
//   -- Job de vencimiento (paso 8): buscar rápido las Held vencidas.
//   CREATE INDEX ix_reservations_expiration
//       ON reservations (expires_at)
//       WHERE status = 'Held';
//
// En EF Core (los nombres de columna del filtro tienen que coincidir con los reales):
//   b.Property(r => r.Status).HasConversion<string>();
//   b.Property(r => r.RejectionReason).HasConversion<string>();
//   b.HasIndex(r => new { r.EventId, r.SeatId }).IsUnique()
//    .HasFilter("status IN ('Held', 'Confirmed')");
//   b.HasIndex(r => r.RequestId).IsUnique();
//   b.HasIndex(r => r.ExpiresAt).HasFilter("status = 'Held'");
//
// Para distinguir qué índice se violó: capturar DbUpdateException cuyo InnerException
// es PostgresException con SqlState == "23505" y mirar ConstraintName.
public interface IReservationRepository
{
    // Devuelve la reserva generada por esa solicitud, o null si todavía no existe.
    Task<Reservation?> GetByRequestIdAsync(Guid requestId, CancellationToken ct);

    // Inserta una reserva en estado Held.
    //   - Created:          se insertó.
    //   - SeatOccupied:     violó ux_reservations_active_seat.
    //   - DuplicateRequest: violó ux_reservations_request (otra réplica ya la procesó).
    // No debe lanzar excepción en esos dos casos de conflicto.
    Task<HoldResult> TryHoldAsync(Reservation reservation, CancellationToken ct);

    // Inserta una reserva ya Rejected (por asiento ocupado), para que el usuario pueda
    // consultar el motivo. No choca con el índice de asientos porque su estado no es
    // Held ni Confirmed. Devuelve false si ya existía una reserva para esa solicitud.
    Task<bool> AddRejectedAsync(Reservation reservation, CancellationToken ct);

    // Cambia el estado SOLO si la reserva sigue en el estado esperado:
    //   UPDATE reservations
    //   SET status = @to, rejection_reason = @reason, updated_at = @now
    //   WHERE id = @id AND status = @from;
    // Devuelve true si actualizó una fila.
    // Así el resultado del pago y el job de vencimiento no se pisan entre sí:
    // el que llega segundo no actualiza nada y se entera por el false.
    Task<bool> TryChangeStatusAsync(
        Guid reservationId,
        ReservationStatus from,
        ReservationStatus to,
        RejectionReason? reason,
        DateTimeOffset now,
        CancellationToken ct);

    // Pasa a Expired todas las Held cuyo plazo terminó, en una sola sentencia:
    //   UPDATE reservations SET status = 'Expired', updated_at = @now
    //   WHERE status = 'Held' AND expires_at <= @now
    //   RETURNING *;
    // Devuelve las reservas que venció (para los logs de auditoría).
    Task<IReadOnlyList<Reservation>> ExpireHeldAsync(DateTimeOffset now, CancellationToken ct);
}

public enum HoldResult
{
    Created,
    SeatOccupied,
    DuplicateRequest,
}
