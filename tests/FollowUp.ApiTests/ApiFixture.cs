using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FollowUp.ApiTests;

/// <summary>
/// Boots the real API pipeline in-process (TestServer) against the live dev database (FOLLOWUP_DB).
/// Acquires the seeded admin token ONCE so individual tests don't each hammer the login rate limiter.
/// Everything is skipped when FOLLOWUP_DB is absent, keeping database-less CI green.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "Seed_Admin_2026!";

    public bool DatabaseAvailable { get; }
    public bool AuthReady { get; private set; }
    public string? AdminToken { get; private set; }

    private readonly string? _connectionString;

    public ApiFixture()
    {
        _connectionString = Environment.GetEnvironmentVariable("FOLLOWUP_DB");
        DatabaseAvailable = !string.IsNullOrWhiteSpace(_connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                // A deterministic signing secret so token issuance works even if the env var is unset.
                ["Auth:SigningSecret"] = Environment.GetEnvironmentVariable("FOLLOWUP_AUTH_SECRET")
                    ?? "api-contract-tests-signing-secret-0123456789abcdef",
            };
            if (!string.IsNullOrWhiteSpace(_connectionString))
                overrides["ConnectionStrings:FollowUp"] = _connectionString;
            config.AddInMemoryCollection(overrides);
        });
    }

    public async Task InitializeAsync()
    {
        if (!DatabaseAvailable) return;
        try
        {
            using var client = CreateClient();
            var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = AdminUser, password = AdminPassword });
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
                AdminToken = body?.Token;
                AuthReady = !string.IsNullOrEmpty(AdminToken);
            }
        }
        catch
        {
            AuthReady = false;
        }
    }

    /// <summary>An HttpClient carrying the cached admin bearer token.</summary>
    public HttpClient CreateAuthedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", AdminToken);
        return client;
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public sealed record LoginResponse(string Token, string ExpiresAt, string Username, string RoleName);
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture> { }
