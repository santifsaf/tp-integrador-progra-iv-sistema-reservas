using Availability.Application.Abstractions;
using Availability.Domain.Entities;
using Availability.Domain.Enums;
using Availability.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Availability.Infrastructure.Repositories;

public sealed class ReservationRequestRepository(AvailabilityDbContext db) : IReservationRequestRepository
{
    public async Task SaveAsync(ReservationRequest request, CancellationToken ct)
    {
        db.ReservationRequests.Add(request);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid id, RequestStatus status, CancellationToken ct)
    {
        // La actualización directa modifica únicamente Status y no completa ni reemplaza la entidad.
        await db.ReservationRequests
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, status), ct);
        // El comportamiento para un Id inexistente queda pendiente de U-03.
    }

    public Task<ReservationRequest?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.ReservationRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ReservationRequest>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        await db.ReservationRequests
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(ct);
}
