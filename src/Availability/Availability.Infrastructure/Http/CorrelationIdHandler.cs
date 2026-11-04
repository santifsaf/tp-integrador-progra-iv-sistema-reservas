using Microsoft.AspNetCore.Http;

namespace Availability.Infrastructure.Http;

internal sealed class CorrelationIdHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    private const string CorrelationIdItemName = "CorrelationId";
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        var correlationId = httpContextAccessor.HttpContext?.Items[CorrelationIdItemName]?.ToString();
        if (!string.IsNullOrWhiteSpace(correlationId) &&
            !request.Headers.Contains(CorrelationIdHeaderName))
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdHeaderName, correlationId);
        }

        return base.SendAsync(request, ct);
    }
}
