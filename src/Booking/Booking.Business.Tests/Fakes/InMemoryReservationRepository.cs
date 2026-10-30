using Booking.Data;
using Booking.Data.Entities;

namespace Booking.Business.Tests.Fakes;

// Repositorio en memoria que imita las restricciones de la base:
//   - una reserva por RequestId (ux_reservations_request)
//   - un asiento Held/Confirmed por evento (ux_reservations_active_seat)
//   - cambios de estado condicionales
// El lock simula la atomicidad que en Postgres da el índice único.
internal sealed class InMemoryReservationRepository : IReservationRepository
{
    private readonly object _lock = new();
    private readonly List<Reservation> _reservations = [];

    public IReadOnlyList<Reservation> All
    {
        get { lock (_lock) return _reservations.Select(Copy).ToList(); }
    }

    public void Add(Reservation reservation)
    {
        lock (_lock) _reservations.Add(Copy(reservation));
    }

    // Simula la carrera entre dos réplicas: la primera búsqueda de esa solicitud no la
    // encuentra (todavía no estaba), pero cuando se intenta insertar ya existe.
    public Guid? HideOnFirstLookup { get; set; }

    public Task<Reservation?> GetByRequestIdAsync(Guid requestId, CancellationToken ct)
    {
        lock (_lock)
        {
            if (HideOnFirstLookup == requestId)
            {
                HideOnFirstLookup = null;
                return Task.FromResult<Reservation?>(null);
            }

            var r = _reservations.FirstOrDefault(x => x.RequestId == requestId);
            return Task.FromResult(r is null ? null : Copy(r));
        }
    }

    public Task<HoldResult> TryHoldAsync(Reservation reservation, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_reservations.Any(x => x.RequestId == reservation.RequestId))
                return Task.FromResult(HoldResult.DuplicateRequest);

            if (_reservations.Any(x => x.EventId == reservation.EventId
                                       && x.SeatId == reservation.SeatId
                                       && x.Status is ReservationStatus.Held or ReservationStatus.Confirmed))
                return Task.FromResult(HoldResult.SeatOccupied);

            _reservations.Add(Copy(reservation));
            return Task.FromResult(HoldResult.Created);
        }
    }

    public Task<bool> AddRejectedAsync(Reservation reservation, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_reservations.Any(x => x.RequestId == reservation.RequestId))
                return Task.FromResult(false);

            _reservations.Add(Copy(reservation));
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryChangeStatusAsync(Guid reservationId, ReservationStatus from, ReservationStatus to,
        RejectionReason? reason, DateTimeOffset now, CancellationToken ct)
    {
        lock (_lock)
        {
            var r = _reservations.FirstOrDefault(x => x.Id == reservationId && x.Status == from);
            if (r is null) return Task.FromResult(false);

            r.Status = to;
            r.RejectionReason = reason;
            r.UpdatedAt = now;
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<Reservation>> ExpireHeldAsync(DateTimeOffset now, CancellationToken ct)
    {
        lock (_lock)
        {
            var expired = _reservations
                .Where(x => x.Status == ReservationStatus.Held && x.ExpiresAt <= now)
                .ToList();

            foreach (var r in expired)
            {
                r.Status = ReservationStatus.Expired;
                r.UpdatedAt = now;
            }

            return Task.FromResult<IReadOnlyList<Reservation>>(expired.Select(Copy).ToList());
        }
    }

    // Se devuelven copias, como haría una base: modificar el objeto devuelto no cambia lo guardado.
    private static Reservation Copy(Reservation r) => new()
    {
        Id = r.Id,
        RequestId = r.RequestId,
        UserId = r.UserId,
        EventId = r.EventId,
        SeatId = r.SeatId,
        Status = r.Status,
        RejectionReason = r.RejectionReason,
        CreatedAt = r.CreatedAt,
        ExpiresAt = r.ExpiresAt,
        UpdatedAt = r.UpdatedAt,
    };
}
