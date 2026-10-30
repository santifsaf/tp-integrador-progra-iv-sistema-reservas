namespace Availability.Infrastructure.Options;

public sealed class ServiceBusOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string TopicName { get; set; } = string.Empty;
}
