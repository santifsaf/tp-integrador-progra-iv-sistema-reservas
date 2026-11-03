using Booking.Api.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Booking.Api.Tests;

public sealed class PaymentClientConfigurationTests
{
    [Fact]
    public void Gateway_ResolvesWithoutContactingPaymentOrKeycloak()
    {
        var services = Services();
        HttpClient? tokenClient = null;
        services.AddHttpClient<PaymentTokenProvider>()
            .ConfigureHttpClient(client => tokenClient = client)
            .ConfigurePrimaryHttpMessageHandler(() => new RejectNetworkHandler());
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<GrpcPaymentGateway>());
        Assert.NotNull(tokenClient);
        Assert.Equal(TimeSpan.FromSeconds(7), tokenClient.Timeout);
    }

    [Theory]
    [InlineData("Address", "not-a-url")]
    [InlineData("TokenEndpoint", "ftp://identity.example/token")]
    [InlineData("ClientId", "")]
    [InlineData("ClientSecret", "")]
    [InlineData("DeadlineSeconds", "0")]
    [InlineData("TokenTimeoutSeconds", "0")]
    public void InvalidConfiguration_IsReportedWhenGatewayIsRequested(string key, string value)
    {
        using var provider = Services(key, value).BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<GrpcPaymentGateway>());

        Assert.Contains($"Payment:{key}", error.Message);
        Assert.DoesNotContain("test-secret", error.Message);
    }

    private static IServiceCollection Services(string? key = null, string? value = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Payment:Address"] = "https://payment.example",
            ["Payment:TokenEndpoint"] = "https://identity.example/token",
            ["Payment:ClientId"] = "booking",
            ["Payment:ClientSecret"] = "test-secret",
            ["Payment:DeadlineSeconds"] = "5",
            ["Payment:TokenTimeoutSeconds"] = "7",
        };
        if (key is not null) values[$"Payment:{key}"] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddPaymentCommunication(configuration);
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Resolver el gateway no debe iniciar peticiones.");
    }
}
