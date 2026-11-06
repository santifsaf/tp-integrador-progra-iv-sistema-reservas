using Booking.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Booking.Api.Tests;

public sealed class BookingAuthenticationTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    [InlineData("Testing", true)]
    public void HttpsMetadata_DefaultDependsOnEnvironment(string environment, bool expected)
    {
        using var services = CreateServices(environment);

        Assert.Equal(expected, Options(services).RequireHttpsMetadata);
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    public void HttpsMetadata_CanBeConfiguredExplicitly(string environment, bool required)
    {
        using var services = CreateServices(environment, new()
        {
            ["Authentication:RequireHttpsMetadata"] = required.ToString(),
        });

        Assert.Equal(required, Options(services).RequireHttpsMetadata);
    }

    [Fact]
    public void LocalDocker_UsesInternalMetadataAndPreservesIssuerAndTokenValidation()
    {
        const string issuer = "http://localhost:8081/realms/seat-reservation";
        const string metadata = "http://keycloak:8081/realms/seat-reservation/.well-known/openid-configuration";
        using var services = CreateServices("Production", new()
        {
            ["Authentication:Authority"] = issuer,
            ["Authentication:MetadataAddress"] = metadata,
            ["Authentication:RequireHttpsMetadata"] = "false",
        });

        // Se ejecuta la configuración real de JwtBearer sin reemplazar su
        // ConfigurationManager. No se hace una petición de red a Keycloak.
        var options = Options(services);

        Assert.False(options.RequireHttpsMetadata);
        Assert.Equal(metadata, options.MetadataAddress);
        Assert.Equal(issuer, options.Authority);
        Assert.Equal(issuer, options.TokenValidationParameters.ValidIssuer);
        Assert.Equal("booking", options.TokenValidationParameters.ValidAudience);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);
    }

    [Fact]
    public void Production_RejectsHttpMetadataWithoutExplicitOverride()
    {
        using var services = CreateServices("Production", new()
        {
            ["Authentication:MetadataAddress"] = "http://keycloak:8081/realms/seat-reservation/.well-known/openid-configuration",
        });

        Assert.Throws<InvalidOperationException>(() => Options(services));
    }

    private static JwtBearerOptions Options(ServiceProvider services) =>
        services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

    private static ServiceProvider CreateServices(string environment,
        Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "https://identity.example/realms/seat-reservation",
            ["Authentication:Audience"] = "booking",
            ["Authentication:AllowedClientId"] = "availability",
        };
        if (overrides is not null)
            foreach (var item in overrides) values[item.Key] = item.Value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBookingAuthentication(configuration, new TestEnvironment(environment));
        return services.BuildServiceProvider();
    }

    private sealed class TestEnvironment(string environment) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;
        public string ApplicationName { get; set; } = "Booking.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
