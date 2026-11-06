using Availability.Domain.Enums;

namespace Availability.Application.Common;

public static class RequestStatusNames
{
    public const string Pending = "PENDING";
    public const string Failed = "FAILED";

    public static string FromLocal(RequestStatus status) => status switch
    {
        RequestStatus.Received or RequestStatus.Published => Pending,
        RequestStatus.PublishFailed => Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
