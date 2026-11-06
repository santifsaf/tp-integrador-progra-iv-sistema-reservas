using Availability.Application.Abstractions;
using Availability.Application.Dtos;
using Availability.Domain.Entities;
using Availability.Domain.Enums;

namespace Availability.Application.Common;

public sealed class RequestStatusResolver
{
    private const string PublishFailedDescription = "No se pudo publicar la solicitud";
    private const string NeverPublishedDescription = "La solicitud no llegó a publicarse";
    private static readonly TimeSpan NeverPublishedThreshold = TimeSpan.FromMinutes(5);

    private readonly IBookingClient _bookingClient;
    private readonly TimeProvider _timeProvider;

    public RequestStatusResolver(IBookingClient bookingClient, TimeProvider timeProvider)
    {
        _bookingClient = bookingClient;
        _timeProvider = timeProvider;
    }

    public async Task<RequestStatusDto> ResolveAsync(ReservationRequest request, CancellationToken ct)
    {
        if (request.Status == RequestStatus.PublishFailed)
        {
            return new RequestStatusDto(request.Id, RequestStatusNames.Failed, PublishFailedDescription, request.CreatedAt, request.UpdatedAt);
        }

        var bookingStatus = await _bookingClient.GetStatusByRequestIdAsync(request.Id, ct);

        if (bookingStatus is null)
        {
            if (request.Status == RequestStatus.Received
                && _timeProvider.GetUtcNow().UtcDateTime - request.CreatedAt > NeverPublishedThreshold)
            {
                return new RequestStatusDto(request.Id, RequestStatusNames.Failed, NeverPublishedDescription, request.CreatedAt, request.UpdatedAt);
            }

            return new RequestStatusDto(request.Id, RequestStatusNames.Pending, null, request.CreatedAt, request.UpdatedAt);
        }

        return new RequestStatusDto(request.Id, bookingStatus.Status.ToUpperInvariant(), bookingStatus.Description, request.CreatedAt, request.UpdatedAt);
    }
}
