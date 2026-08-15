using FollowUp.Application;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Builds the real Application + Infrastructure DI graph against the live dev database (from FOLLOWUP_DB).
/// A test <see cref="ICurrentUser"/> stands in for the API-provided one. Tests are skipped when FOLLOWUP_DB
/// is not set, so CI without a database stays green.
/// </summary>
public sealed class IntegrationFixture : IDisposable
{
    public ServiceProvider Services { get; }
    public bool DatabaseAvailable { get; }

    public IntegrationFixture()
    {
        var connectionString = Environment.GetEnvironmentVariable("FOLLOWUP_DB");
        DatabaseAvailable = !string.IsNullOrWhiteSpace(connectionString);
        if (!DatabaseAvailable)
        {
            Services = new ServiceCollection().BuildServiceProvider();
            return;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FollowUp"] = connectionString,
                ["Auth:SigningSecret"] = "integration-test-signing-secret-value-0123456789",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(config);
        services.AddScoped<ICurrentUser>(_ => TestCurrentUser);
        Services = services.BuildServiceProvider();
    }

    public static readonly TestUser TestCurrentUser = new();

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        // Clean slate for deterministic assertions. TRUNCATE is refused by the append-only trigger, so we
        // DELETE — the audit purge is permitted only under the GUC, set in the SAME batch/connection.
        await db.Database.ExecuteSqlRawAsync(@"
SET followup.allow_audit_purge='on';
DELETE FROM outbox_message;
DELETE FROM laboratory;
DELETE FROM audit_entry;");
    }

    public void Dispose() => Services.Dispose();
}

public sealed class TestUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public AppUserId UserId { get; } = AppUserId.New();
    public string Username => "integration-tester";
    public RoleId RoleId { get; } = RoleId.New();
    public UserSessionId? SessionId => null;
    public IReadOnlySet<string> Privileges { get; } = new HashSet<string>(Domain.Identity.Privileges.All);
    public OrgScope Scope => OrgScope.Global;
    public RepresentativeId? RepresentativeId => null;
    public string? Ip => "127.0.0.1";
    public string? CorrelationId => "itest-corr";
    public bool Has(string privilege) => true;
}

[CollectionDefinition("integration")]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture> { }
