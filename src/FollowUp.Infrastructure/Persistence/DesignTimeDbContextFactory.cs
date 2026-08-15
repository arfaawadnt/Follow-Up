using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FollowUp.Infrastructure.Persistence;

/// <summary>
/// Enables <c>dotnet ef migrations</c> at design time without running the API. The connection string is a
/// placeholder — migrations are generated from the model, not a live database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FollowUpDbContext>
{
    public FollowUpDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FollowUpDbContext>()
            .UseNpgsql("Host=localhost;Database=followup;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new FollowUpDbContext(options);
    }
}
