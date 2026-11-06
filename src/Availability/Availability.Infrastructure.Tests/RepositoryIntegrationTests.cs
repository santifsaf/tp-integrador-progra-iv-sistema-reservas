using Availability.Application.Abstractions;
using Availability.Domain.Entities;
using Availability.Domain.Enums;
using Availability.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Availability.Infrastructure.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class RepositoryIntegrationTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task SaveAsync_WhenIdAlreadyExists_ThrowsDatabaseUpdateException()
    {
        var id = Guid.NewGuid();
        await using (var db = postgres.CreateDbContext())
        {
            var repository = new ReservationRequestRepository(
                db,
                NullLogger<ReservationRequestRepository>.Instance);
            await repository.SaveAsync(CreateRequest(id), CancellationToken.None);
        }

        await using var duplicateDb = postgres.CreateDbContext();
        var duplicateRepository = new ReservationRequestRepository(
            duplicateDb,
            NullLogger<ReservationRequestRepository>.Instance);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            duplicateRepository.SaveAsync(CreateRequest(id), CancellationToken.None));
    }

    [Theory]
    [InlineData(RequestStatus.Received)]
    [InlineData(RequestStatus.Published)]
    [InlineData(RequestStatus.PublishFailed)]
    public async Task UpdateStatusAsync_PersistsEachStatus(RequestStatus status)
    {
        var request = CreateRequest(Guid.NewGuid());
        await SaveAsync(request);

        await using var db = postgres.CreateDbContext();
        var repository = new ReservationRequestRepository(
            db,
            NullLogger<ReservationRequestRepository>.Instance);
        await repository.UpdateStatusAsync(request.Id, status, CancellationToken.None);

        await using var verificationDb = postgres.CreateDbContext();
        var saved = await verificationDb.ReservationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(status, saved.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_WhenIdDoesNotExist_ThrowsKeyNotFoundException()
    {
        await using var db = postgres.CreateDbContext();
        var repository = new ReservationRequestRepository(
            db,
            NullLogger<ReservationRequestRepository>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            repository.UpdateStatusAsync(Guid.NewGuid(), RequestStatus.Received, CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalseWhenSeatBelongsToAnotherEvent()
    {
        await using var db = postgres.CreateDbContext();
        var repository = new SeatRepository(db);
        var firstEvent = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondEvent = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var firstEventSeat = Guid.Parse("11111111-1111-1111-1111-111111111101");

        Assert.True(await repository.ExistsAsync(firstEvent, firstEventSeat, CancellationToken.None));
        Assert.False(await repository.ExistsAsync(secondEvent, firstEventSeat, CancellationToken.None));
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsOnlyThatUsersRequestsInStableOrder()
    {
        var userId = Guid.NewGuid();
        var otherUserRequest = CreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc));
        var tieBreakSecond = CreateRequest(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            userId,
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        var newest = CreateRequest(
            Guid.NewGuid(),
            userId,
            new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        var tieBreakFirst = CreateRequest(
            Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            userId,
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        await SaveAsync(otherUserRequest, tieBreakSecond, newest, tieBreakFirst);

        await using var db = postgres.CreateDbContext();
        var repository = new ReservationRequestRepository(
            db,
            NullLogger<ReservationRequestRepository>.Instance);

        var requests = await repository.GetByUserIdAsync(userId, CancellationToken.None);

        Assert.Equal(
            new[] { newest.Id, tieBreakFirst.Id, tieBreakSecond.Id },
            requests.Select(request => request.Id));
    }

    [Fact]
    public async Task Seed_RecreatesTheSameEventsAndSeats()
    {
        var firstDatabaseName = $"availability_seed_first_{Guid.NewGuid():N}";
        await using (var firstDb = postgres.CreateDbContext(firstDatabaseName))
        {
            await firstDb.Database.EnsureCreatedAsync();
        }

        var firstSnapshot = await ReadSeedAsync(firstDatabaseName);

        var secondDatabaseName = $"availability_seed_second_{Guid.NewGuid():N}";
        await using (var secondDb = postgres.CreateDbContext(secondDatabaseName))
        {
            await secondDb.Database.EnsureCreatedAsync();
        }

        var secondSnapshot = await ReadSeedAsync(secondDatabaseName);
        Assert.Equal(firstSnapshot.Events, secondSnapshot.Events);
        Assert.Equal(firstSnapshot.Seats, secondSnapshot.Seats);
        Assert.Contains(firstSnapshot.Events, item => item.Id == Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.Contains(firstSnapshot.Seats, item => item.EventId == Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    private async Task SaveAsync(params ReservationRequest[] requests)
    {
        await using var db = postgres.CreateDbContext();
        foreach (var request in requests)
        {
            db.ReservationRequests.Add(request);
        }

        await db.SaveChangesAsync();
    }

    private async Task<(SeedEvent[] Events, SeedSeat[] Seats)> ReadSeedAsync(string? databaseName = null)
    {
        await using var db = postgres.CreateDbContext(databaseName);
        var events = await db.Events.AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => new SeedEvent(item.Id, item.Name, item.StartsAt, item.PriceCents, item.Currency))
            .ToArrayAsync();
        var seats = await db.Seats.AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => new SeedSeat(item.Id, item.EventId, item.Label))
            .ToArrayAsync();
        return (events, seats);
    }

    private static ReservationRequest CreateRequest(
        Guid id,
        Guid? userId = null,
        DateTime? createdAt = null) =>
        new()
        {
            Id = id,
            UserId = userId ?? Guid.NewGuid(),
            EventId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SeatId = Guid.Parse("11111111-1111-1111-1111-111111111101"),
            Status = RequestStatus.Received,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = createdAt ?? DateTime.UtcNow
        };

    private sealed record SeedEvent(Guid Id, string Name, DateTime StartsAt, long PriceCents, string Currency);

    private sealed record SeedSeat(Guid Id, Guid EventId, string Label);
}
