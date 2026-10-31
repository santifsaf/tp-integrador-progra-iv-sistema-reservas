using Booking.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Booking.Data.Persistence;

public sealed class ReservationRepository(BookingDbContext context) : IReservationRepository
{
    public async Task<Reservation?> GetByRequestIdAsync(Guid requestId, CancellationToken ct)
    {
        // Sin tracking: la capa de negocio no debe mutar la entidad aquí.
        return await context.Reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RequestId == requestId, ct);
    }

    public async Task<HoldResult> TryHoldAsync(Reservation reservation, CancellationToken ct)
    {
        try
        {
            context.Add(reservation);
            await context.SaveChangesAsync(ct);
            return HoldResult.Created;
        }
        catch (DbUpdateException ex) when (TryGetUniqueViolationConstraintName(ex) is not null)
        {
            var constraintName = TryGetUniqueViolationConstraintName(ex)!;

            // Descartar la entidad del contexto. El llamador (la capa de negocio) reutiliza
            // el mismo objeto Reservation en AddRejectedAsync, y si queda trackeada aquí,
            // el siguiente Add/Insert falla. Detached permite que la capa superior la
            // agregue como nueva entidad.
            context.Entry(reservation).State = EntityState.Detached;

            // Distinguir qué índice se violó. Postgres puede reportar cualquiera de los dos
            // (o ambos si un reintento choca con los dos), así que consultamos la base en
            // lugar de confiar en ConstraintName.
            var existing = await GetByRequestIdAsync(reservation.RequestId, ct);
            if (existing is not null)
            {
                return HoldResult.DuplicateRequest;
            }

            // Sin RequestId duplicado, debe ser el índice de asientos (concurrencia).
            // Si violó ActiveSeatIndex, es porque ya hay un Held/Confirmed en ese asiento.
            if (constraintName == ReservationConfiguration.ActiveSeatIndex)
            {
                return HoldResult.SeatOccupied;
            }

            // Otra violación de unicidad inesperada: propagar.
            throw;
        }
    }

    public async Task<bool> AddRejectedAsync(Reservation reservation, CancellationToken ct)
    {
        if (reservation.Status != ReservationStatus.Rejected)
        {
            throw new ArgumentException(
                $"La reserva debe estar en estado Rejected, pero está en {reservation.Status}.",
                nameof(reservation));
        }

        try
        {
            context.Add(reservation);
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (TryGetUniqueViolationConstraintName(ex) is not null)
        {
            var constraintName = TryGetUniqueViolationConstraintName(ex)!;

            // Descartar la entidad del contexto.
            context.Entry(reservation).State = EntityState.Detached;

            // Si violó el índice de RequestId, significa que esa solicitud ya tenía reserva.
            if (constraintName == ReservationConfiguration.RequestIndex)
            {
                return false;
            }

            // Otra violación inesperada: propagar.
            throw;
        }
    }

    public async Task<bool> TryResolveHeldAsync(
        Guid reservationId,
        ReservationStatus to,
        RejectionReason? reason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Validar destino.
        if (to is not (ReservationStatus.Confirmed or ReservationStatus.Rejected))
        {
            throw new ArgumentOutOfRangeException(
                nameof(to),
                $"Solo se puede resolver a Confirmed o Rejected, no a {to}.");
        }

        // Si es Rejected, el motivo es obligatorio.
        if (to == ReservationStatus.Rejected && reason is null)
        {
            throw new ArgumentException(
                "RejectionReason es obligatorio cuando el destino es Rejected.",
                nameof(reason));
        }

        // Normalizar reason: si es Confirmed, guardar null.
        var finalReason = to == ReservationStatus.Confirmed ? null : reason;

        // Actualización atómica: una sola sentencia con todas las validaciones en el WHERE.
        var affectedRows = await context.Reservations
            .Where(r => r.Id == reservationId && r.Status == ReservationStatus.Held && r.ExpiresAt > now)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, to)
                    .SetProperty(r => r.RejectionReason, finalReason)
                    .SetProperty(r => r.UpdatedAt, now),
                ct);

        return affectedRows == 1;
    }

    public async Task<IReadOnlyList<Reservation>> ExpireHeldAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Una sola sentencia atómica: el UPDATE RETURNING materializa el resultado.
        // Si dos réplicas ejecutan este job simultáneamente, ninguna fila se vence ni
        // se devuelve dos veces: PostgreSQL garantiza que cada fila afectada aparece
        // en el RETURNING de UN SOLO UPDATE.
        // 
        // El límite es complemento exacto de TryResolveHeldAsync:
        // - TryResolveHeldAsync: WHERE ... AND expires_at > now (no resuelve si vencen)
        // - ExpireHeldAsync: WHERE ... AND expires_at <= now (vence si no fueron resueltos)
        var expiredReservations = await context.Reservations
            .FromSqlInterpolated($@"
                UPDATE reservations 
                SET status = {ReservationStatus.Expired.ToString()}, updated_at = {now}
                WHERE status = {ReservationStatus.Held.ToString()} AND expires_at <= {now}
                RETURNING *
            ")
            .AsNoTracking()
            .ToListAsync(ct);

        return expiredReservations;
    }

    /// <summary>
    /// Extrae el nombre del constraint si la excepción es una violación de unicidad de Postgres.
    /// Devuelve null si no es una violación de unicidad.
    /// </summary>
    private static string? TryGetUniqueViolationConstraintName(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return pgEx.ConstraintName;
        }

        return null;
    }
}
