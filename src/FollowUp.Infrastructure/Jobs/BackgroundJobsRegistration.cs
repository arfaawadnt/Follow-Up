using FollowUp.Application.Common.Abstractions;
using FollowUp.Infrastructure.Emailing;
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
        services.AddScoped<NightlyStatsSyncJob>();
        services.AddScoped<RetentionJob>();
        services.AddScoped<MonthlySegmentAssignmentJob>();
        services.AddScoped<StatsEmailJobRunner>();

        services.AddHostedService<RecurringJobsInitializer>();
        return services;
    }
}

/// <summary>Registers the recurring schedules once the host (and Hangfire storage) is available.</summary>
public sealed class RecurringJobsInitializer : IHostedService
{
    private readonly IRecurringJobManager _jobs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringJobsInitializer> _logger;

    public RecurringJobsInitializer(IRecurringJobManager jobs, IServiceScopeFactory scopeFactory, ILogger<RecurringJobsInitializer> logger)
    {
        _jobs = jobs;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var cairo = ResolveCairo();
        var cairoOptions = new RecurringJobOptions { TimeZone = cairo };

        // Evening missed-sweep BEFORE midnight archive (JOBS-001), then the midnight roll-over.
        _jobs.AddOrUpdate<MissedSweepJob>("missed-visit-sweep", j => j.RunAsync(CancellationToken.None), "0 22 * * *", cairoOptions);
        _jobs.AddOrUpdate<BoardRolloverJob>("board-rollover", j => j.RunAsync(CancellationToken.None), "0 0 * * *", cairoOptions);
        // Notification dispatcher (outbox drain) — frequent; retention nightly; oracle hourly (runner gates on interval).
        _jobs.AddOrUpdate<NotificationDispatchJob>("notification-dispatcher", j => j.RunAsync(CancellationToken.None), "*/1 * * * *");
        _jobs.AddOrUpdate<OracleSyncJob>("oracle-sync", j => j.RunAsync(CancellationToken.None), "0 * * * *");
        // Statistics: one nightly job pulls the previous day from Oracle for Test, Lab and Detailed stats over the
        // SAME window in one pass, so the three pages can never drift on coverage or snapshot timing (full history
        // is still seeded on demand via the page buttons).
        _jobs.AddOrUpdate<NightlyStatsSyncJob>("nightly-stats-sync", j => j.RunAsync(CancellationToken.None), "0 0 * * *", cairoOptions);
        // Decommission the former standalone, separately-scheduled stats jobs — all three now ride the job above.
        _jobs.RemoveIfExists("teststats-sync");
        _jobs.RemoveIfExists("labstats-sync");
        _jobs.RemoveIfExists("detailedstats-sync");
        _jobs.AddOrUpdate<RetentionJob>("retention-purge", j => j.RunAsync(CancellationToken.None), "0 3 * * *", cairoOptions);
        // Month-start segment auto-assignment — 02:00 on the 1st (Cairo), after that night's stats pull has synced
        // the previous month's final day, so the just-ended month's achieved income is complete.
        _jobs.AddOrUpdate<MonthlySegmentAssignmentJob>("monthly-segment-assignment", j => j.RunAsync(CancellationToken.None), "0 2 1 * *", cairoOptions);

        // Per-subscription daily statistics-email schedules (each has its own send time).
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IStatsEmailScheduler>().SyncAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconcile stats-email schedules on startup.");
        }

        _logger.LogInformation("Recurring jobs registered (Cairo timezone).");
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
