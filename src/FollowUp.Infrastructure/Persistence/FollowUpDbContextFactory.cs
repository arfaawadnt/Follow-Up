using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FollowUp.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> (migrations). Configures the context exactly like the runtime
/// (Npgsql + snake_case) but WITHOUT running the API's startup pipeline, so migration scaffolding never touches
/// a live database or applies migrations as a side effect. Migrations add does not open a connection.
/// </summary>
public sealed class FollowUpDbContextFactory : IDesignTimeDbContextFactory<FollowUpDbContext>
{
    public FollowUpDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("FOLLOWUP_DB")
            ?? "Host=localhost;Port=5432;Database=followup;Username=followup;Password=design-time";
        var options = new DbContextOptionsBuilder<FollowUpDbContext>()
            .UseNpgsql(conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new FollowUpDbContext(options);
    }
}
