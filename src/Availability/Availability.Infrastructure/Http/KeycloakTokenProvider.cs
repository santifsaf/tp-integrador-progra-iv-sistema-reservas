using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Availability.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Availability.Infrastructure.Http;

internal sealed class KeycloakTokenProvider(
    IHttpClientFactory factory,
    IOptions<KeycloakOptions> options,
    TimeProvider clock,
    ILogger<KeycloakTokenProvider> logger)
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

        // Revalidar al adquirir el semáforo evita que llamadas concurrentes renueven el token varias veces.
        await _gate.WaitAsync(ct);
        try
        {
            if (IsValid())
            {
                return _token!;
            }

            var http = factory.CreateClient(HttpClientNames.Keycloak);
            var startTimestamp = Stopwatch.GetTimestamp();
            logger.LogInformation(
                "Keycloak token operation {Service} {Operation} {Event}",
                "Availability",
                "AcquireKeycloakToken",
                "Started");

            int? statusCode = null;
            try
            {
                using var response = await http.PostAsync(
                    _options.TokenEndpoint,
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials",
                        ["client_id"] = _options.ClientId,
                        ["client_secret"] = _options.ClientSecret
                    }),
                    ct);
                statusCode = (int)response.StatusCode;
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                    ?? throw new InvalidOperationException("La respuesta de token est� vac�a.");

                _token = body.AccessToken;
                _expiresAt = clock.GetUtcNow().AddSeconds(body.ExpiresIn);
                logger.LogInformation(
                    "Keycloak token operation {Service} {Operation} {Event} {DurationMs} {StatusCode}",
                    "Availability",
                    "AcquireKeycloakToken",
                    "Completed",
                    Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                    statusCode);
                return _token;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Keycloak token operation {Service} {Operation} {Event} {DurationMs} {StatusCode}",
                    "Availability",
                    "AcquireKeycloakToken",
                    "Failed",
                    Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                    statusCode);
                throw;
            }
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
