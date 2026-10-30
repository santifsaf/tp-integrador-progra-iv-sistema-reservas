using Booking.Data;
using Booking.Data.Entities;

namespace Booking.Demo.Simulation;

// Reemplaza a Postgres en el simulador. Imita las restricciones que va a tener la base real:
//   - una reserva por RequestId                       (ux_reservations_request)
//   - un solo Held/Confirmed por asiento de un evento  (ux_reservations_active_seat)
//   - cambios de estado condicionales                  (UPDATE ... WHERE status = @from)
// El lock hace de la atomicidad que en Postgres da el índice único: aunque las tres
// réplicas intenten insertar a la vez, solo una gana.
public sealed class InMemoryReservationRepository : IReservationRepository
{
    private readonly object _lock = new();
    private readonly List<Reservation> _rows = [];

    public IReadOnlyList<Reservation> Snapshot()
    {
        lock (_lock) return _rows.Select(Copy).OrderBy(r => r.CreatedAt).ToList();
    }

    public Task<Reservation?> GetByRequestIdAsync(Guid requestId, CancellationToken ct)
    {
        lock (_lock)
        {
            var row = _rows.FirstOrDefault(r => r.RequestId == requestId);
            return Task.FromResult(row is null ? null : Copy(row));
        }
    }

    public async Task<HoldResult> TryHoldAsync(Reservation reservation, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(5, 30), ct); // latencia de red/base

        lock (_lock)
        {
            if (_rows.Any(r => r.RequestId == reservation.RequestId))
                return HoldResult.DuplicateRequest;

            if (_rows.Any(r => r.EventId == reservation.EventId
                               && r.SeatId == reservation.SeatId
                               && r.Status is ReservationStatus.Held or ReservationStatus.Confirmed))
                return HoldResult.SeatOccupied;

            _rows.Add(Copy(reservation));
            return HoldResult.Created;
        }
    }

    public Task<bool> AddRejectedAsync(Reservation reservation, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_rows.Any(r => r.RequestId == reservation.RequestId)) return Task.FromResult(false);
            _rows.Add(Copy(reservation));
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryChangeStatusAsync(Guid reservationId, ReservationStatus from, ReservationStatus to,
        RejectionReason? reason, DateTimeOffset now, CancellationToken ct)
    {
        lock (_lock)
        {
            var row = _rows.FirstOrDefault(r => r.Id == reservationId && r.Status == from);
            if (row is null) return Task.FromResult(false);
            row.Status = to;
            row.RejectionReason = reason;
            row.UpdatedAt = now;
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<Reservation>> ExpireHeldAsync(DateTimeOffset now, CancellationToken ct)
    {
        lock (_lock)
        {
            var expired = _rows.Where(r => r.Status == ReservationStatus.Held && r.ExpiresAt <= now).ToList();
            foreach (var row in expired)
            {
                row.Status = ReservationStatus.Expired;
                row.UpdatedAt = now;
            }
            return Task.FromResult<IReadOnlyList<Reservation>>(expired.Select(Copy).ToList());
        }
    }

    private static Reservation Copy(Reservation r) => new()
    {
        Id = r.Id, RequestId = r.RequestId, UserId = r.UserId, EventId = r.EventId, SeatId = r.SeatId,
        Status = r.Status, RejectionReason = r.RejectionReason,
        CreatedAt = r.CreatedAt, ExpiresAt = r.ExpiresAt, UpdatedAt = r.UpdatedAt,
    };
}
