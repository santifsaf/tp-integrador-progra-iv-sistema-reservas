using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Booking.Api.Payments;

public sealed class PaymentTokenProvider(HttpClient client, IOptions<PaymentClientOptions> options)
{
    private readonly PaymentClientOptions _options = options.Value;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        // Client credentials autentica a Booking, sin reutilizar el token del usuario
        // ni el de Availability. Esta versión obtiene un token por operación.
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
        });
        using var response = await client.PostAsync(_options.TokenEndpoint, form, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("No se pudo obtener el token de Booking.", null, response.StatusCode);

        TokenResponse? token;
        try
        {
            token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        }
        catch (JsonException)
        {
            // No se incorpora el cuerpo recibido ni el secreto a los errores o logs.
            throw new HttpRequestException("La respuesta de autenticación no es válida.");
        }

        if (string.IsNullOrWhiteSpace(token?.AccessToken)
            || token.AccessToken.Any(char.IsWhiteSpace)
            || !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException("La respuesta de autenticación no contiene un token Bearer válido.");

        return token.AccessToken;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }
    }
}
