using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Availability.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Availability.Infrastructure.Http;

internal sealed class KeycloakTokenProvider(
    IHttpClientFactory factory,
    IOptions<KeycloakOptions> options,
    TimeProvider clock)
{
    private readonly KeycloakOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (IsValid())
        {
            return _token!;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (IsValid())
            {
                return _token!;
            }

            var http = factory.CreateClient(HttpClientNames.Keycloak);
            using var response = await http.PostAsync(
                _options.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret
                }),
                ct);

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new InvalidOperationException("La respuesta de token está vacía.");

            _token = body.AccessToken;
            _expiresAt = clock.GetUtcNow().AddSeconds(body.ExpiresIn);
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsValid() =>
        _token is not null &&
        clock.GetUtcNow() < _expiresAt - TimeSpan.FromSeconds(30);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

internal static class HttpClientNames
{
    public const string Keycloak = "Keycloak";
}
