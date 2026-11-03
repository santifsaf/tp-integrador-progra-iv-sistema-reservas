using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Availability.Application.Abstractions;
using Availability.Application.Common;
using Availability.Application.Dtos;

namespace Availability.Infrastructure.Http;

internal sealed class BookingClient(IHttpClientFactory httpClientFactory) : IBookingClient
{
    private const string CorrelationIdHeaderName = "X-Correlation-ID";
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Booking");

    public async Task<IReadOnlySet<Guid>> GetOccupiedSeatIdsAsync(Guid eventId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/events/{eventId:D}/occupied-seats");
        using var response = await SendAsync(request, "occupied seats", ct);
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

        return items
            .OfType<OccupiedSeatResponse>()
            .Where(item => item.IsOccupied)
            .Select(item => item.SeatId)
            .ToHashSet();
    }

    public async Task<BookingReservationStatusDto?> GetStatusByRequestIdAsync(
        Guid requestId,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/reservations/by-request/{requestId:D}");
        request.Headers.Add(CorrelationIdHeaderName, requestId.ToString("D"));

        using var response = await SendAsync(request, "reservation status", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
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
    }

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
