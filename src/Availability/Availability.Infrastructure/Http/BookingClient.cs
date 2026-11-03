using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Availability.Infrastructure.Http;

internal sealed class BookingClient(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<BookingClient> logger) : IBookingClient
{
    private const string CorrelationIdHeaderName = "X-Correlation-ID";
    private const string CorrelationIdItemName = "CorrelationId";
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Booking");

    public Task<IReadOnlySet<Guid>> GetOccupiedSeatIdsAsync(Guid eventId, CancellationToken ct) =>
        ExecuteAsync(
            "GetOccupiedSeatIds",
            GetHttpCorrelationId(),
            async setStatusCode =>
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"api/events/{eventId:D}/occupied-seats");
                using var response = await SendAsync(request, "occupied seats", ct);
                setStatusCode((int)response.StatusCode);
                EnsureSuccessStatusCode(response, "occupied seats");

                var items = await ReadResponseAsync<List<OccupiedSeatResponse?>>(
                    response,
                    "occupied seats",
                    ct);

                if (items.Any(item => item is null))
                {
                    throw new ExternalServiceUnavailableException(
                        "Booking returned an invalid response for occupied seats.",
                        new JsonException("The response contains a null seat."));
                }

                IReadOnlySet<Guid> seatIds = items
                    .OfType<OccupiedSeatResponse>()
                    .Where(item => item.IsOccupied)
                    .Select(item => item.SeatId)
                    .ToHashSet();
                return seatIds;
            });

    public Task<BookingReservationStatusDto?> GetStatusByRequestIdAsync(
        Guid requestId,
        CancellationToken ct) =>
        ExecuteAsync(
            "GetStatusByRequestId",
            requestId.ToString("D"),
            async setStatusCode =>
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"api/reservations/by-request/{requestId:D}");
                request.Headers.Add(CorrelationIdHeaderName, requestId.ToString("D"));

                using var response = await SendAsync(request, "reservation status", ct);
                var statusCode = (int)response.StatusCode;
                setStatusCode(statusCode);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // Booking todavía no persistió la reserva; Application lo interpreta como pendiente.
                    return null;
                }

                EnsureSuccessStatusCode(response, "reservation status");

                var status = await ReadResponseAsync<BookingReservationStatusDto>(
                    response,
                    "reservation status",
                    ct);

                if (string.IsNullOrWhiteSpace(status.Status))
                {
                    throw new ExternalServiceUnavailableException(
                        "Booking returned a reservation status without a status value.",
                        new JsonException("The status field is missing or empty."));
                }

                return status;
            });

    private async Task<T> ExecuteAsync<T>(
        string operation,
        string? correlationId,
        Func<Action<int>, Task<T>> action)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["Service"] = "Availability",
            ["Operation"] = operation,
            ["CorrelationId"] = correlationId
        });
        logger.LogInformation(
            "Booking client operation {Service} {Operation} {Event} {CorrelationId}",
            "Availability",
            operation,
            "Started",
            correlationId);

        int? statusCode = null;
        try
        {
            var result = await action(code => statusCode = code);
            logger.LogInformation(
                "Booking client operation {Service} {Operation} {Event} {CorrelationId} {DurationMs} {StatusCode}",
                "Availability",
                operation,
                "Completed",
                correlationId,
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                statusCode);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Booking client operation {Service} {Operation} {Event} {CorrelationId} {DurationMs} {StatusCode}",
                "Availability",
                operation,
                "Failed",
                correlationId,
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                statusCode);
            throw;
        }
    }

    private string? GetHttpCorrelationId() =>
        httpContextAccessor.HttpContext?.Items[CorrelationIdItemName]?.ToString();

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken ct)
    {
        try
        {
            return await _httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalServiceUnavailableException(
                $"Could not query Booking for {operation}.",
                ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ExternalServiceUnavailableException(
                $"The Booking request for {operation} timed out.",
                ex);
        }
    }

    private static void EnsureSuccessStatusCode(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalServiceUnavailableException(
                $"Booking returned HTTP {(int)response.StatusCode} while querying {operation}.");
        }
    }

    private static async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken ct)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct)
                ?? throw new JsonException("The response body is empty.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            throw new ExternalServiceUnavailableException(
                $"Booking returned an invalid response for {operation}.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalServiceUnavailableException(
                $"Could not read Booking's response for {operation}.",
                ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ExternalServiceUnavailableException(
                $"Reading Booking's response for {operation} timed out.",
                ex);
        }
    }

    private sealed record OccupiedSeatResponse(Guid SeatId, bool IsOccupied);
}
