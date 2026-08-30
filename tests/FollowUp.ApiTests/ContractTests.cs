using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace FollowUp.ApiTests;

/// <summary>
/// HTTP-level contract tests: they exercise the real routing, auth middleware, rate limiter, validation
/// pipeline, and idempotency behavior end-to-end through the in-process TestServer.
/// </summary>
[Collection("api")]
public sealed class ContractTests
{
    private readonly ApiFixture _fx;
    public ContractTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Health_live_is_public_and_ok()
    {
        using var client = _fx.CreateClient();
        var resp = await client.GetAsync("/healthz/live");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_is_401()
    {
        using var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v1/labs");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public void Login_with_seeded_admin_returns_a_token()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        Skip.IfNot(_fx.AuthReady, "Seeded admin credentials did not authenticate.");
        _fx.AdminToken.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task Bad_password_is_401()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        using var client = _fx.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "definitely-wrong" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Authenticated_lab_list_is_ok()
    {
        Skip.IfNot(_fx.AuthReady, "Auth not ready.");
        using var client = _fx.CreateAuthedClient();
        var resp = await client.GetAsync("/api/v1/labs?pageSize=1");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Create_lab_then_fetch_it_round_trips()
    {
        Skip.IfNot(_fx.AuthReady, "Auth not ready.");
        using var client = _fx.CreateAuthedClient();
        var code = $"MGL-CT{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var create = await client.PostAsJsonAsync("/api/v1/labs",
            new { code, name = "Contract Test Lab", segment = "A", governorate = "Cairo", workDays = Array.Empty<string>(), visitTimes = Array.Empty<string>() });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await create.Content.ReadFromJsonAsync<IdResponse>();
        created!.Id.Should().NotBeEmpty();

        var get = await client.GetAsync($"/api/v1/labs/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Invalid_create_payload_is_400()
    {
        Skip.IfNot(_fx.AuthReady, "Auth not ready.");
        using var client = _fx.CreateAuthedClient();
        // Empty code + name violate the validator.
        var resp = await client.PostAsJsonAsync("/api/v1/labs",
            new { code = "", name = "", segment = "Z", workDays = Array.Empty<string>(), visitTimes = Array.Empty<string>() });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Idempotency_key_replays_the_same_response()
    {
        Skip.IfNot(_fx.AuthReady, "Auth not ready.");
        using var client = _fx.CreateAuthedClient();
        var key = $"ct-idem-{Guid.NewGuid():N}";
        var code = $"MGL-ID{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var payload = new { code, name = "Idem Contract Lab", segment = "B", workDays = Array.Empty<string>(), visitTimes = Array.Empty<string>() };

        async Task<IdResponse?> Post()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/labs") { Content = JsonContent.Create(payload) };
            req.Headers.Add("Idempotency-Key", key);
            var resp = await client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
            return await resp.Content.ReadFromJsonAsync<IdResponse>();
        }

        var first = await Post();
        var second = await Post(); // same key + same payload -> replayed, no duplicate-code 409
        second!.Id.Should().Be(first!.Id);
    }

    [SkippableFact]
    public async Task Login_endpoint_is_rate_limited()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        // Dedicated host so this burst's limiter state can't make the sibling login tests flaky.
        using var factory = _fx.WithWebHostBuilder(_ => { });
        using var client = factory.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 15; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "wrong-on-purpose" });
            codes.Add(resp.StatusCode);
        }
        codes.Should().Contain(HttpStatusCode.TooManyRequests, "the fixed-window login limiter should reject once the burst exceeds the permit");
    }

    [SkippableFact]
    public async Task Esign_sign_endpoint_is_rate_limited()
    {
        // SIG-9: signing re-authenticates a password (like login) yet carried no throttle, leaving it a
        // credential-guessing surface. The limiter runs before auth, so an unauthenticated burst still trips it
        // once the endpoint declares the "esign" policy; without the policy every request is a plain 401.
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        using var factory = _fx.WithWebHostBuilder(_ => { });
        using var client = factory.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 15; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/v1/esign/complaint/x/sign",
                new { meaning = "Approval", reason = (string?)null, password = "wrong-on-purpose" });
            codes.Add(resp.StatusCode);
        }
        codes.Should().Contain(HttpStatusCode.TooManyRequests, "signing must be throttled to blunt password guessing (SIG-9)");
    }

    [SkippableFact]
    public async Task Retired_stage_endpoint_returns_410_gone()
    {
        // CMP-5: /complaints/{id}/stage duplicated /advance and is retired; it answers 410 to any caller
        // (anonymous, so the tombstone is testable) pointing them to /advance.
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        using var client = _fx.CreateClient();
        var resp = await client.PostAsJsonAsync($"/api/v1/complaints/{Guid.NewGuid()}/stage", new { stage = "Investigation" });
        resp.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    private sealed record IdResponse(Guid Id);
}
