using System.Net;
using System.Text;
using Booking.Api.Payments;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Xunit;

namespace Booking.Api.Tests;

public sealed class PaymentTokenProviderTests
{
    [Fact]
    public async Task RequestsBookingClientCredentialsAsFormAndReadsBearerToken()
    {
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://identity.example/token", request.RequestUri?.AbsoluteUri);
            Assert.Equal("application/x-www-form-urlencoded", request.Content?.Headers.ContentType?.MediaType);
            var fields = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("client_credentials", fields["grant_type"].ToString());
            Assert.Equal("booking", fields["client_id"].ToString());
            Assert.Equal("test+secret", fields["client_secret"].ToString());
            Assert.Equal(3, fields.Count);
            return Json("{\"access_token\":\"returned-token\",\"token_type\":\"bearer\"}");
        }));

        var token = await Provider(http).GetTokenAsync(CancellationToken.None);

        Assert.Equal("returned-token", token);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(503)]
    public async Task ErrorResponse_DoesNotExposeResponseBody(int status)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("private-response test+secret") })));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).GetTokenAsync(CancellationToken.None));

        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.DoesNotContain("private-response", error.ToString());
        Assert.DoesNotContain("test+secret", error.ToString());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"\",\"token_type\":\"Bearer\"}")]
    [InlineData("{\"access_token\":\"bad token\",\"token_type\":\"Bearer\"}")]
    [InlineData("{\"access_token\":\"test-token\",\"token_type\":\"Other\"}")]
    public async Task InvalidTokenResponse_IsRejected(string body)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(body))));

        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((_, ct) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(ct);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(http).GetTokenAsync(cancellation.Token));
    }

    private static PaymentTokenProvider Provider(HttpClient http) => new(http, Options.Create(new PaymentClientOptions
    {
        TokenEndpoint = "https://identity.example/token", ClientId = "booking", ClientSecret = "test+secret",
    }));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => reply(request, ct);
    }
}
