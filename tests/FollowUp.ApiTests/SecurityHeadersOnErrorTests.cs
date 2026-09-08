using System.Net;
using System.Text;
using FluentAssertions;
using Xunit;

namespace FollowUp.ApiTests;

/// <summary>
/// Finding PLT-013: the ExceptionHandlingMiddleware calls <c>Response.Clear()</c> before writing the
/// RFC 7807 body, which wiped the baseline security headers and the correlation id from every error
/// response. The security/correlation headers are now applied via <c>OnStarting</c>, so they survive
/// the clear and are present on 4xx/5xx responses too — not only on the happy path.
/// </summary>
[Collection("api")]
public sealed class SecurityHeadersOnErrorTests
{
    private readonly ApiFixture _fx;
    public SecurityHeadersOnErrorTests(ApiFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Error_responses_still_carry_security_and_correlation_headers()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        using var client = _fx.CreateClient();

        // Malformed JSON to the anonymous login endpoint → binding failure → 400 through the
        // exception pipeline, which is exactly the Response.Clear() path PLT-013 is about.
        using var body = new StringContent("{ not valid json", Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/api/v1/auth/login", body);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "malformed JSON is a client error");
        resp.Headers.Contains("X-Content-Type-Options").Should()
            .BeTrue("the nosniff security header must survive Response.Clear() on an error response");
        resp.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        resp.Headers.Contains("X-Frame-Options").Should().BeTrue();
        resp.Headers.Contains("Content-Security-Policy").Should().BeTrue();
        resp.Headers.Contains("X-Correlation-ID").Should()
            .BeTrue("the correlation id must be present on error responses for support to trace them");
    }
}
