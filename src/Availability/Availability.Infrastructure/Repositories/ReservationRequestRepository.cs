using Availability.Application.Abstractions;
using Availability.Domain.Entities;
using Availability.Domain.Enums;
using Availability.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Availability.Infrastructure.Repositories;

public sealed class ReservationRequestRepository(
    AvailabilityDbContext db,
    ILogger<ReservationRequestRepository> logger) : IReservationRequestRepository
{
    public async Task SaveAsync(ReservationRequest request, CancellationToken ct)
    {
        await ExecuteWriteAsync("SaveReservation", request.Id, async () =>
        {
            db.ReservationRequests.Add(request);
            await db.SaveChangesAsync(ct);
        });
    }

    public async Task UpdateStatusAsync(Guid id, RequestStatus status, CancellationToken ct)
    {
        await ExecuteWriteAsync("UpdateReservationStatus", id, async () =>
        {
            var rows = await db.ReservationRequests
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, status), ct);

            if (rows == 0)
            {
                throw new KeyNotFoundException($"No existe la solicitud {id}.");
            }
        });
    }

    private async Task ExecuteWriteAsync(string operation, Guid requestId, Func<Task> action)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = requestId
        });

        logger.LogInformation(
            "Infrastructure operation {Event} {Operation} {CorrelationId} {RequestId}",
            "Started",
            operation,
            requestId,
            requestId);

        try
        {
            await action();
            logger.LogInformation(
                "Infrastructure operation {Event} {Operation} {CorrelationId} {RequestId} {DurationMs}",
                "Completed",
                operation,
                requestId,
                requestId,
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Infrastructure operation {Event} {Operation} {CorrelationId} {RequestId} {DurationMs}",
                "Failed",
                operation,
                requestId,
                requestId,
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            throw;
        }
    }

    public Task<ReservationRequest?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.ReservationRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ReservationRequest>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        await db.ReservationRequests
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
}
