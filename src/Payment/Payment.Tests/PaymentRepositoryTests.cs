
using Microsoft.EntityFrameworkCore;
using Payment.Data;
using Payment.Data.Entities;
using Payment.Data.Repositories;
using Testcontainers.PostgreSql;
using Xunit;

namespace Payment.Tests
{
    //Prueba de integracion para PaymentRepository con container (docker)
    public class PaymentRepositoryTests : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .Build();

        public Task InitializeAsync() => _postgres.StartAsync();
        public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();


        [Fact]
        //Comprobar que si se reintenta agregar un pago con el mismo OperationId, no se duplica
        public async Task TryAddAsync_MismoOperationId_NoDuplicados()
        {
            await using var db = NewContext();
            await db.Database.EnsureCreatedAsync();
            var repo = new PaymentRepository(db);

            var firts = await repo.TryAddAsync(NewPayment("op-1"));
            var retry = await repo.TryAddAsync(NewPayment("op-1")); // Reintento de Booking


            Assert.True(firts);
            Assert.False(retry);
            Assert.Equal(1, await db.Payments.CountAsync(p => p.OperationId == "op-1"));
        }

        [Fact]
        //Comprobar que si se intenta agregar un pago con el mismo Id pero diferente OperationId, 
        //se produce un error real de violacion de unicidad
        public async Task TryAddAsync_MismoIdConDiferenteOperationId_ErrorReal()
        {
            await using var db = NewContext();
            await db.Database.EnsureCreatedAsync();
            var repo = new PaymentRepository(db);

            var original = NewPayment("op-2");
            await repo.TryAddAsync(original);

            var conflicto = NewPayment("op-3", original.Id);

            await using var db2 = NewContext();
            var repo2 = new PaymentRepository(db2);
            await Assert.ThrowsAsync<DbUpdateException>(() => repo2.TryAddAsync(conflicto));
        }


        private PaymentDbContext NewContext()
        {
            var options = new DbContextOptionsBuilder<PaymentDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;
            return new PaymentDbContext(options);
        }

        private static PaymentRecord NewPayment(string operationId, Guid? id = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            ReservationId = "res-1",
            OperationId = operationId,
            AmountCents = 1000,
            Currency = "ARS",
            Status = PaymentStatus.PENDING,
            CreatedAt = DateTime.UtcNow
        };
    }
}
