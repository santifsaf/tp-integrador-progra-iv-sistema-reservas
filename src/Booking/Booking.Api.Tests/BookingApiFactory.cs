using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Booking.Data;
using Booking.Data.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Booking.Api.Tests;

public sealed class BookingApiFactory : WebApplicationFactory<Program>
{
    internal const string Issuer = "http://localhost:8081/realms/seat-reservation";
    private readonly RSA _rsa = RSA.Create(2048);
    internal QueryTestRepository Repository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // El host de pruebas escribe en consola, sin tocar el Event Log de Windows.
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IReservationRepository>(Repository);
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Se usa el validador JWT real. Solo la metadata y clave pública de
                // Keycloak se sustituyen para que las pruebas no dependan de la red.
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "booking-tests" });
                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });
        });
    }

    internal string Token(string clientId = "availability", string audience = "booking",
        string issuer = Issuer, bool expired = false, bool invalidSignature = false)
    {
        using var wrongRsa = invalidSignature ? RSA.Create(2048) : null;
        var key = new RsaSecurityKey(wrongRsa ?? _rsa) { KeyId = "booking-tests" };
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(issuer, audience,
            [new Claim("sub", "service-account-availability"), new Claim("azp", clientId)],
            notBefore: now.AddMinutes(-20),
            expires: expired ? now.AddMinutes(-10) : now.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _rsa.Dispose();
    }
}

// Solo se registra en el host de pruebas. Las escrituras no forman parte del caso
// de uso de consulta y deben fallar si este endpoint intenta realizarlas.
internal sealed class QueryTestRepository : IReservationRepository
{
    internal static readonly Guid OccupiedEventId = new("00000000-0000-0000-0000-000000000100");
    internal static readonly Guid FailureEventId = new("00000000-0000-0000-0000-000000000199");
    internal static readonly Guid[] OccupiedSeatIds =
    [
        new("00000000-0000-0000-0000-000000000101"),
        new("00000000-0000-0000-0000-000000000102"),
    ];
    internal static readonly Guid FailureRequestId = new("00000000-0000-0000-0000-000000000099");
    internal static Guid RequestIdFor(ReservationStatus status) =>
        new($"00000000-0000-0000-0000-{(int)status + 1:000000000000}");

    public Task<Reservation?> GetByRequestIdAsync(Guid requestId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (requestId == FailureRequestId) throw new InvalidOperationException("internal-database-error");

        foreach (var status in Enum.GetValues<ReservationStatus>())
        {
            if (requestId != RequestIdFor(status)) continue;
            return Task.FromResult<Reservation?>(new Reservation
            {
                Id = Guid.NewGuid(), RequestId = requestId, Status = status,
                RejectionReason = status == ReservationStatus.Rejected ? RejectionReason.SeatOccupied : null,
            });
        }
        return Task.FromResult<Reservation?>(null);
    }

    public Task<IReadOnlyList<Guid>> GetOccupiedSeatIdsAsync(Guid eventId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (eventId == FailureEventId) throw new InvalidOperationException("internal-database-error");
        return Task.FromResult<IReadOnlyList<Guid>>(eventId == OccupiedEventId ? OccupiedSeatIds : []);
    }

    public Task<HoldResult> TryHoldAsync(Reservation reservation, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> AddRejectedAsync(Reservation reservation, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> TryResolveHeldAsync(Guid reservationId, ReservationStatus to, RejectionReason? reason,
        DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Reservation>> ExpireHeldAsync(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
}
