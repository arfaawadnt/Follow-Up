using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.ApiTests;

/// <summary>
/// Finding IAM-010: SignalR passes the bearer token in the hub URL query (?access_token=…). The framework's
/// "Request starting {url}" line (Information) would carry it, so Microsoft.AspNetCore logging is pinned to
/// Warning (appsettings Serilog override) to keep that line — and the token — out of the logs.
/// UseSerilogRequestLogging itself logs RequestPath only, without the query. This asserts the app's merged
/// configuration keeps that override, guarding the mitigation against a regression (lowering the level would
/// re-expose the token in logs). Asserting config rather than the runtime logger keeps it deterministic —
/// Serilog's global logger is closed by sibling WebApplicationFactory disposals.
/// </summary>
[Collection("api")]
public sealed class LogsExcludeAccessTokenTests
{
    private readonly ApiFixture _fx;
    public LogsExcludeAccessTokenTests(ApiFixture fx) => _fx = fx;

    [SkippableFact]
    public void Aspnetcore_request_logging_is_pinned_to_warning()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var config = _fx.Services.GetRequiredService<IConfiguration>();

        config["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"].Should().Be("Warning",
            "the framework's 'Request starting <url>' log carries ?access_token= from the hub handshake and must stay suppressed (IAM-010)");
    }
}
