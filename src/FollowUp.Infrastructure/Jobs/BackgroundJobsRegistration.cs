using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// Registers Hangfire (PostgreSQL storage) and the recurring jobs (ADR-0004). Kept separate from
/// AddInfrastructure so unit/integration tests can compose the app without starting a job server.
/// The API calls <see cref="AddBackgroundJobs"/> during startup.
/// </summary>
public static class BackgroundJobsRegistration
{
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FollowUp");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = Environment.GetEnvironmentVariable("FOLLOWUP_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("No database connection string for Hangfire storage.");

        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer(options => options.SchedulePollingInterval = TimeSpan.FromSeconds(5));

        services.AddScoped<BoardRolloverJob>();
        services.AddScoped<MissedSweepJob>();
        services.AddScoped<NotificationDispatchJob>();
        services.AddScoped<OracleSyncJob>();
        services.AddScoped<TestStatsSyncJob>();
        services.AddScoped<LabStatsSyncJob>();
        services.AddScoped<RetentionJob>();

        services.AddHostedService<RecurringJobsInitializer>();
        return services;
    }
}

/// <summary>Registers the recurring schedules once the host (and Hangfire storage) is available.</summary>
public sealed class RecurringJobsInitializer : IHostedService
{
    private readonly IRecurringJobManager _jobs;
    private readonly ILogger<RecurringJobsInitializer> _logger;

    public RecurringJobsInitializer(IRecurringJobManager jobs, ILogger<RecurringJobsInitializer> logger)
    {
        _jobs = jobs;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cairo = ResolveCairo();
        var cairoOptions = new RecurringJobOptions { TimeZone = cairo };

        // Evening missed-sweep BEFORE midnight archive (JOBS-001), then the midnight roll-over.
        _jobs.AddOrUpdate<MissedSweepJob>("missed-visit-sweep", j => j.RunAsync(CancellationToken.None), "0 22 * * *", cairoOptions);
        _jobs.AddOrUpdate<BoardRolloverJob>("board-rollover", j => j.RunAsync(CancellationToken.None), "0 0 * * *", cairoOptions);
        // Notification dispatcher (outbox drain) — frequent; retention nightly; oracle hourly (runner gates on interval).
        _jobs.AddOrUpdate<NotificationDispatchJob>("notification-dispatcher", j => j.RunAsync(CancellationToken.None), "*/1 * * * *");
        _jobs.AddOrUpdate<OracleSyncJob>("oracle-sync", j => j.RunAsync(CancellationToken.None), "0 * * * *");
        // Statistics: each pulls just the previous day from Oracle around midnight Cairo (full history is
        // seeded on demand via the page buttons). Staggered a few minutes apart to spread the Oracle load.
        _jobs.AddOrUpdate<TestStatsSyncJob>("teststats-sync", j => j.RunAsync(CancellationToken.None), "0 0 * * *", cairoOptions);
        _jobs.AddOrUpdate<LabStatsSyncJob>("labstats-sync", j => j.RunAsync(CancellationToken.None), "5 0 * * *", cairoOptions);
        _jobs.AddOrUpdate<RetentionJob>("retention-purge", j => j.RunAsync(CancellationToken.None), "0 3 * * *", cairoOptions);

        _logger.LogInformation("Recurring jobs registered (Cairo timezone).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static TimeZoneInfo ResolveCairo()
    {
        foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
