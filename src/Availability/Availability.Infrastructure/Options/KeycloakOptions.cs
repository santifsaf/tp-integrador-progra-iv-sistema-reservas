namespace Availability.Infrastructure.Options;

public sealed class KeycloakOptions
{
    public string TokenEndpoint { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
