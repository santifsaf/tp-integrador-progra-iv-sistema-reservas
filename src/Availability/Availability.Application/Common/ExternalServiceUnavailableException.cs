namespace Availability.Application.Common;

public class ExternalServiceUnavailableException : Exception
{
    public ExternalServiceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
