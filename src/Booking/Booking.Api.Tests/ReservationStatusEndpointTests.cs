using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Booking.Data;
using Booking.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Booking.Api.Tests;

public sealed class ReservationStatusEndpointTests(BookingApiFactory factory) : IClassFixture<BookingApiFactory>
{
    [Theory]
    [InlineData(ReservationStatus.Held, "HELD")]
    [InlineData(ReservationStatus.Confirmed, "CONFIRMED")]
    [InlineData(ReservationStatus.Rejected, "REJECTED")]
    [InlineData(ReservationStatus.Expired, "EXPIRED")]
    public async Task KnownRequest_ReturnsTheAgreedJson(ReservationStatus status, string expected)
    {
        using var client = AuthorizedClient();

        var response = await client.GetAsync(Path(QueryTestRepository.RequestIdFor(status)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("status").GetString());
        var description = json.RootElement.GetProperty("description");
        if (status == ReservationStatus.Rejected) Assert.Equal("El asiento está ocupado.", description.GetString());
        else Assert.Equal(JsonValueKind.Null, description.ValueKind);
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task UnknownRequest_Returns404ProblemDetails()
    {
        using var client = AuthorizedClient();
        var response = await client.GetAsync(Path(Guid.NewGuid()));
        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidRequestId_Returns400(string requestId)
    {
        using var client = AuthorizedClient();
        var response = await client.GetAsync($"/api/reservations/by-request/{requestId}");
        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MissingToken_Returns401()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(Path(QueryTestRepository.FailureRequestId));
        await AssertProblem(response, HttpStatusCode.Unauthorized);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("signature")]
    [InlineData("malformed")]
    public async Task InvalidToken_Returns401(string scenario)
    {
        var token = scenario switch
        {
            "expired" => factory.Token(expired: true),
            "audience" => factory.Token(audience: "payment"),
            "issuer" => factory.Token(issuer: "https://other.example/realm"),
            "signature" => factory.Token(invalidSignature: true),
            _ => "invalid-token",
        };
        using var client = AuthorizedClient(token);
        var response = await client.GetAsync(Path(QueryTestRepository.FailureRequestId));
        await AssertProblem(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidTokenFromAnotherClient_Returns403()
    {
        using var client = AuthorizedClient(factory.Token(clientId: "another-service"));
        var response = await client.GetAsync(Path(QueryTestRepository.FailureRequestId));
        await AssertProblem(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RepositoryFailure_Returns500WithoutInternalDetails()
    {
        using var client = AuthorizedClient();
        var response = await client.GetAsync(Path(QueryTestRepository.FailureRequestId));
        var body = await AssertProblem(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("internal-database-error", body);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepositoryNotConfigured_Returns503InsteadOfPretendingRequestIsMissing()
    {
        using var withoutRepository = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.RemoveAll<IReservationRepository>()));
        using var client = withoutRepository.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        var response = await client.GetAsync(Path(Guid.NewGuid()));
        await AssertProblem(response, HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    public async Task ExceptionHandler_PreservesRequestIdInErrorLog(
        bool repositoryMissing, HttpStatusCode expected)
    {
        var logs = new ExceptionLogCollector();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            if (repositoryMissing)
                builder.ConfigureTestServices(services => services.RemoveAll<IReservationRepository>());
        });
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        var requestId = repositoryMissing ? Guid.NewGuid() : QueryTestRepository.FailureRequestId;

        var response = await client.GetAsync(Path(requestId));

        await AssertProblem(response, expected);
        // Se inspecciona el campo estructurado del manejador, no el log de Business,
        // para detectar la pérdida del identificador al limpiar la ruta del request.
        var entry = Assert.Single(logs.Entries);
        Assert.Equal(requestId.ToString(), entry["RequestId"]?.ToString());
    }

    [Fact]
    public async Task Health_RemainsAvailableWithoutAToken()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient AuthorizedClient(string? token = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? factory.Token());
        return client;
    }

    private static string Path(Guid requestId) => $"/api/reservations/by-request/{requestId}";

    private static async Task<string> AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal((int)expected, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
        return body;
    }

    private sealed class ExceptionLogCollector : ILoggerProvider
    {
        public ConcurrentQueue<IReadOnlyDictionary<string, object?>> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) =>
            categoryName == "Booking.Api.Errors.BookingExceptionHandler"
                ? new ErrorLogger(Entries)
                : NullLogger.Instance;

        public void Dispose() { }

        private sealed class ErrorLogger(
            ConcurrentQueue<IReadOnlyDictionary<string, object?>> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel) && state is IEnumerable<KeyValuePair<string, object?>> properties)
                    entries.Enqueue(properties.ToDictionary(property => property.Key, property => property.Value));
            }
        }
    }
}
