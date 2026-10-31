using System.Net.Http.Headers;

namespace Availability.Infrastructure.Http;

internal sealed class BookingAuthHandler(KeycloakTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await tokens.GetTokenAsync(ct));

        return await base.SendAsync(request, ct);
    }
}
