using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Availability.Infrastructure.Tests;

public sealed class BookingClientTests
{
    [Fact]
    public async Task GetOccupiedSeatIdsAsync_ReturnsOnlyOccupiedSeatsAndHandlesEmptyList()
    {
        var occupiedSeatId = Guid.NewGuid();
        var freeSeatId = Guid.NewGuid();
        var occupiedResponseCount = 0;
        var provider = CreateProvider(
            _ => Interlocked.Increment(ref occupiedResponseCount) == 1
                ? JsonResponse(HttpStatusCode.OK,
                    $"[{{\"seatId\":\"{occupiedSeatId}\",\"isOccupied\":true}},{{\"seatId\":\"{freeSeatId}\",\"isOccupied\":false}}]")
                : JsonResponse(HttpStatusCode.OK, "[]"));
        await using (provider)
        {
            var client = provider.GetRequiredService<IBookingClient>();
            var occupied = await client.GetOccupiedSeatIdsAsync(Guid.NewGuid(), CancellationToken.None);
            var empty = await client.GetOccupiedSeatIdsAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.Equal(new[] { occupiedSeatId }, occupied);
            Assert.Empty(empty);
        }
    }

    [Fact]
    public async Task GetStatusByRequestIdAsync_ReturnsNullForNotFoundAndThrowsForServerError()
    {
        var notFoundId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var provider = CreateProvider(request => request.RequestUri!.AbsolutePath.EndsWith(
            notFoundId.ToString("D"), StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.NotFound, "")
            : JsonResponse(HttpStatusCode.InternalServerError, ""));
        await using (provider)
        {
            var client = provider.GetRequiredService<IBookingClient>();
            var errorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

            Assert.Null(await client.GetStatusByRequestIdAsync(notFoundId, CancellationToken.None));
            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() =>
                client.GetStatusByRequestIdAsync(errorId, CancellationToken.None));
        }
    }

    [Fact]
    public async Task BookingCalls_ReuseValidTokenAndSendBearerOnEveryRequest()
    {
        var tokenRequests = 0;
        var bookingAuthorizations = new List<AuthenticationHeaderValue?>();
        var provider = CreateProvider(
            request =>
            {
                bookingAuthorizations.Add(request.Headers.Authorization);
                return JsonResponse(HttpStatusCode.OK, "{\"status\":\"CONFIRMED\",\"description\":null}");
            },
            _ =>
            {
                Interlocked.Increment(ref tokenRequests);
                return JsonResponse(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"expires_in\":3600}");
            });
        await using (provider)
        {
            var client = provider.GetRequiredService<IBookingClient>();

            await client.GetStatusByRequestIdAsync(Guid.NewGuid(), CancellationToken.None);
            await client.GetStatusByRequestIdAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.Equal(1, tokenRequests);
            Assert.Equal(2, bookingAuthorizations.Count);
            Assert.All(bookingAuthorizations, authorization =>
            {
                Assert.NotNull(authorization);
                Assert.Equal("Bearer", authorization.Scheme);
                Assert.Equal("test-token", authorization.Parameter);
            });
        }
    }

    private static ServiceProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> bookingHandler,
        Func<HttpRequestMessage, HttpResponseMessage>? keycloakHandler = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:AvailabilityDb"] = "Host=localhost;Database=availability_tests;Username=postgres;Password=postgres",
            ["Booking:BaseUrl"] = "https://booking.test/",
            ["Keycloak:TokenEndpoint"] = "https://keycloak.test/token",
            ["Keycloak:ClientId"] = "availability-tests",
            ["Keycloak:ClientSecret"] = "test-secret"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddHttpClient("Booking")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(bookingHandler));
        services.AddHttpClient("Keycloak")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(
                keycloakHandler ?? (_ => JsonResponse(HttpStatusCode.OK,
                    "{\"access_token\":\"test-token\",\"expires_in\":3600}"))));
        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
