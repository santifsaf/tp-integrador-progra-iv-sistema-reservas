using System.Security.Claims;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Payment.Grpc.Auditing;
using Payment.Grpc.Security;
using Xunit;

namespace Payment.Tests;

public sealed class AllowedClientHandlerTests
{
    private readonly AllowedClientHandler _handler = new();

    [Fact]
    public async Task BookingClient_IsAuthorized()
    {
        var requirement = new AllowedClientRequirement("booking");
        var context = ContextFor(("azp", "booking"), requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task OtherClient_IsNotAuthorized()
    {
        var requirement = new AllowedClientRequirement("booking");
        var context = ContextFor(("azp", "notifications"), requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task ClientIdClaim_IsAcceptedAsFallback()
    {
        var requirement = new AllowedClientRequirement("booking");
        var context = ContextFor(("client_id", "booking"), requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext ContextFor(
        (string Type, string Value) claim,
        params IAuthorizationRequirement[] requirements)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claim.Type, claim.Value)], "Bearer"));
        return new AuthorizationHandlerContext(requirements, principal, resource: null);
    }
}

public sealed class CorrelationIdTests
{
    [Fact]
    public void Resolve_UsesCorrelationIdHeader()
    {
        var headers = new Metadata { { CorrelationId.MetadataKey, "corr-1001" } };

        var result = CorrelationId.Resolve(headers);

        Assert.Equal("corr-1001", result);
    }

    [Fact]
    public void Resolve_AcceptsRequestIdAsFallback()
    {
        var headers = new Metadata { { CorrelationId.RequestIdMetadataKey, "req-2002" } };

        var result = CorrelationId.Resolve(headers);

        Assert.Equal("req-2002", result);
    }

    [Fact]
    public void Resolve_RejectsControlCharactersAndGeneratesOne()
    {
        var headers = new Metadata { { CorrelationId.MetadataKey, "bad\nvalue" } };

        var result = CorrelationId.Resolve(headers);

        Assert.NotEqual("bad\nvalue", result);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void Resolve_UsesFallbackWhenHeaderIsMissing()
    {
        var result = CorrelationId.Resolve(new Metadata(), fallback: "trace-3003");

        Assert.Equal("trace-3003", result);
    }

    [Fact]
    public void Normalize_RejectsIdsLongerThanTheLimit()
    {
        var tooLong = new string('a', CorrelationId.MaxLength + 1);

        Assert.Null(CorrelationId.Normalize(tooLong));
    }
}
