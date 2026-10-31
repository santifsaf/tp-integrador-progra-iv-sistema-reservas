using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Payment.Grpc.Security;
using Xunit;

namespace Payment.Tests;

public sealed class AuthenticationRegistrationTests
{
    [Fact]
    public void AddPaymentAuthentication_ConfiguresKeycloakAudienceAndIssuer()
    {
        var services = BuildServices();

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("payment", options.Audience);
        Assert.Equal("http://localhost:8081/realms/seat-reservation", options.TokenValidationParameters.ValidIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.False(options.MapInboundClaims);
    }

    [Fact]
    public void AddPaymentAuthentication_ProtectsEveryEndpointByDefault()
    {
        var services = BuildServices();

        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(authorization.FallbackPolicy);
        var requirement = Assert.IsType<AllowedClientRequirement>(
            Assert.Single(
                authorization.FallbackPolicy!.Requirements,
                item => item is AllowedClientRequirement));

        // El binder de configuración concatena arrays: si KeycloakOptions trajera un default
        // hardcodeado, acá aparecería "booking" dos veces.
        Assert.Equal(["booking"], requirement.ClientIds);
    }

    [Fact]
    public void AddPaymentAuthentication_RejectsEmptyAuthority()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "",
                ["Keycloak:Audience"] = "payment",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddPaymentAuthentication(configuration));

        Assert.Contains("Keycloak:Authority", exception.Message);
    }

    private static ServiceCollection BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "http://localhost:8081/realms/seat-reservation",
                ["Keycloak:MetadataAddress"] = "http://localhost:8081/realms/seat-reservation/.well-known/openid-configuration",
                ["Keycloak:Audience"] = "payment",
                ["Keycloak:RequireHttpsMetadata"] = "false",
                ["Keycloak:AllowedClients:0"] = "booking",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentAuthentication(configuration);
        return services;
    }
}
