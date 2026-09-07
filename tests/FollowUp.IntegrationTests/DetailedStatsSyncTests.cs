using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Integration;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding B-1 / STAT-001: the detailed-stats window-replace must not delete the window when the Oracle read
/// returns no rows (unconfigured provider or a transient outage), or a failed pull would permanently destroy
/// the window's fee-bearing transaction lines.
/// </summary>
[Collection("integration")]
public sealed class DetailedStatsSyncTests
{
    private readonly IntegrationFixture _fx;
    public DetailedStatsSyncTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Empty_oracle_read_does_not_wipe_the_existing_detailed_window()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);

        // Seed one detailed row inside the window, with the Oracle integration enabled.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var cfg = scope.ServiceProvider.GetRequiredService<IOracleConfigRepository>();
            cfg.Add(OracleConfig.Create(enabled: true, intervalHours: 24));
            db.DetailedRegistrations.Add(DetailedRegistration.Create(
                new DateOnly(2026, 8, 15), "LAB1", "BR1", "ACC1", "Patient", "T1", 0, "Test",
                100m, 0m, "Received", "Done"));
            await db.SaveChangesAsync();
        }

        // Run the detailed sync with a reader that returns NO rows for the window.
        using (var scope = _fx.Services.CreateScope())
        {
            var runner = ActivatorUtilities.CreateInstance<OracleSyncRunner>(
                scope.ServiceProvider, new EmptyOracleReader());
            var result = await runner.RunDetailedStatsAsync(from, to, manual: true, CancellationToken.None);
            result.Status.Should().Be("no-rows");
        }

        // The pre-existing window must be intact — without the guard it would have been deleted.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            (await db.DetailedRegistrations.CountAsync())
                .Should().Be(1, "an empty/failed Oracle read must never delete the existing detailed window");
        }
    }

    private sealed class EmptyOracleReader : IOracleReader
    {
        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OracleRow>>(Array.Empty<OracleRow>());

        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, OracleDateWindow window, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OracleRow>>(Array.Empty<OracleRow>());
    }
}
