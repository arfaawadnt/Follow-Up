using System.Net;
using FluentAssertions;
using FollowUp.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FollowUp.ApiTests;

/// <summary>
/// Finding IAM-006: forwarded headers must be honoured behind a proxy so the per-IP rate limiter and
/// audit trail see the real client — but only from a declared proxy, or a client could spoof its IP.
/// These assert the safe default (loopback only, no spoofable trust) and that a declared trust set
/// replaces the defaults exactly.
/// </summary>
public sealed class ForwardedHeadersSetupTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Default_config_trusts_only_loopback_and_enables_forwarded_processing()
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Configure(options, Config(new()));

        options.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedFor);
        options.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedProto);
        // No proxy declared → the framework's loopback-only trust remains; a remote proxy IP is NOT trusted,
        // so a directly-exposed deployment cannot have its client IP spoofed via X-Forwarded-For.
        options.KnownProxies.Should().Contain(IPAddress.IPv6Loopback);
        options.KnownProxies.Should().NotContain(IPAddress.Parse("10.0.0.5"));
    }

    [Fact]
    public void Declared_proxies_and_networks_replace_the_loopback_defaults()
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Configure(options, Config(new()
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
            ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/12",
            ["ForwardedHeaders:ForwardLimit"] = "2",
        }));

        options.KnownProxies.Should().ContainSingle().Which.Should().Be(IPAddress.Parse("10.0.0.5"));
        options.KnownProxies.Should().NotContain(IPAddress.IPv6Loopback, "an explicit trust set replaces the defaults");
        options.KnownNetworks.Should().ContainSingle();
        options.KnownNetworks[0].Prefix.Should().Be(IPAddress.Parse("172.16.0.0"));
        options.KnownNetworks[0].PrefixLength.Should().Be(12);
        options.ForwardLimit.Should().Be(2);
    }

    [Fact]
    public void Malformed_proxy_or_network_entries_are_ignored()
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Configure(options, Config(new()
        {
            ["ForwardedHeaders:KnownProxies:0"] = "not-an-ip",
            ["ForwardedHeaders:KnownProxies:1"] = "10.0.0.9",
            ["ForwardedHeaders:KnownNetworks:0"] = "garbage",
        }));

        options.KnownProxies.Should().ContainSingle().Which.Should().Be(IPAddress.Parse("10.0.0.9"));
        options.KnownNetworks.Should().BeEmpty();
    }
}
