using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FollowUp.Infrastructure.Persistence;

/// <summary>
/// Enables <c>dotnet ef</c> at design time without running the API. Reads the connection string from the
/// <c>FOLLOWUP_DB</c> environment variable (no secret is committed to source). Falls back to a secret-free
/// local default that expects password-less/dev auth; set <c>FOLLOWUP_DB</c> to point at your database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FollowUpDbContext>
{
    // Secret-free default. The real connection (with credentials) comes from the environment.
    private const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5442;Database=followup;Username=postgres";

    public FollowUpDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FOLLOWUP_DB") ?? DefaultConnectionString;
        var options = new DbContextOptionsBuilder<FollowUpDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new FollowUpDbContext(options);
    }
}
