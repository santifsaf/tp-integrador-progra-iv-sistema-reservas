using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Booking.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Booking.Api.Tests;

public sealed class OccupiedSeatsEndpointTests(BookingApiFactory factory) : IClassFixture<BookingApiFactory>
{
    [Fact]
    public async Task OccupiedSeats_ReturnsAnArrayOfSeatIds()
    {
        using var client = AuthorizedClient();

        var response = await client.GetAsync(Path(QueryTestRepository.OccupiedEventId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        var seats = json.RootElement.EnumerateArray().Select(value => value.GetGuid()).ToArray();
        Assert.Equal(QueryTestRepository.OccupiedSeatIds, seats);
    }

    [Fact]
    public async Task NoOccupiedSeats_Returns200WithEmptyArray()
    {
        using var client = AuthorizedClient();

        var response = await client.GetAsync(Path(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidEventId_Returns400(string eventId)
    {
        using var client = AuthorizedClient();
        await AssertProblem(await client.GetAsync($"/api/events/{eventId}/occupied-seats"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MissingToken_Returns401()
    {
        using var client = factory.CreateClient();
        await AssertProblem(await client.GetAsync(Path(QueryTestRepository.FailureEventId)), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnotherClient_Returns403()
    {
        using var client = AuthorizedClient(factory.Token(clientId: "another-service"));
        await AssertProblem(await client.GetAsync(Path(QueryTestRepository.FailureEventId)), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    public async Task RepositoryFailure_ReturnsProblemAndLogsOneExceptionWithEventId(
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
        var eventId = repositoryMissing ? Guid.NewGuid() : QueryTestRepository.FailureEventId;

        var response = await client.GetAsync(Path(eventId));

        var body = await AssertProblem(response, expected);
        Assert.DoesNotContain("internal-database-error", body);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        var error = Assert.Single(logs.Entries);
        Assert.Equal(eventId.ToString(), error["EventId"]?.ToString());
    }

    private HttpClient AuthorizedClient(string? token = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? factory.Token());
        return client;
    }

    private static string Path(Guid eventId) => $"/api/events/{eventId}/occupied-seats";

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
}
