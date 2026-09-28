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
            // oracle_config is a single-row table that ResetAsync leaves in place — only seed it on a truly fresh database.
            if (!await db.OracleConfigs.AnyAsync())
                scope.ServiceProvider.GetRequiredService<IOracleConfigRepository>().Add(OracleConfig.Create(enabled: true, intervalHours: 24));
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

    /// <summary>2026-09-28: the manual sync (SyncDetailedStatsCommand) runs inside TransactionBehavior's transaction; the
    /// runner must join it instead of opening a nested one (Npgsql: "The connection is already in a transaction").</summary>
    [SkippableFact]
    public async Task Detailed_sync_joins_an_ambient_transaction_instead_of_nesting_one()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        var from = new DateOnly(2026, 8, 1); var to = new DateOnly(2026, 8, 31);

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            if (!await db.OracleConfigs.AnyAsync())
            { scope.ServiceProvider.GetRequiredService<IOracleConfigRepository>().Add(OracleConfig.Create(enabled: true, intervalHours: 24)); await db.SaveChangesAsync(); }

            // The same DbContext the runner uses, already inside a transaction — exactly the manual command's situation.
            await using var outer = await db.Database.BeginTransactionAsync();
            var runner = ActivatorUtilities.CreateInstance<OracleSyncRunner>(scope.ServiceProvider, new OneRowReader());
            var result = await runner.RunDetailedStatsAsync(from, to, manual: true, CancellationToken.None);
            result.Status.Should().Be("ok");
            result.StatsUpserted.Should().Be(1);
            db.Database.CurrentTransaction.Should().BeSameAs(outer, "the runner joined the ambient transaction rather than replacing it");
            await outer.CommitAsync();
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var stored = await db.DetailedRegistrations.SingleAsync(r => r.AccNo == "ACC-AMBIENT");
            stored.RegCreatedAt.Should().Be(new DateTime(2026, 8, 15, 9, 0, 0), "reg.created_date is stored as LDM wall-clock time");
            stored.TestCreatedAt.Should().Be(new DateTime(2026, 8, 15, 13, 0, 0));
            stored.TestAddition.Should().Be(TestAdditionStatus.Over3Hours, "the test was added 4 h after the registration");
        }
    }

    private sealed class OneRowReader : IOracleReader
    {
        private static IReadOnlyList<OracleRow> Rows() => new[]
        {
            new OracleRow(new Dictionary<string, object?>
            {
                ["REG_DT"] = new DateTime(2026, 8, 15), ["LAB_CODE"] = "LAB1", ["REG_BRANCH_CODE"] = "BR1",
                ["ACC_NO"] = "ACC-AMBIENT", ["PATIENT_NAME"] = "Patient", ["TEST_CODE"] = "T1",
                ["TEST_TYPE"] = 0, ["TEST_NAME"] = "Test", ["PATIENT_FEE"] = 100m, ["INSURANCE_FEE"] = 0m,
                ["SAMPLE_STATUS"] = "Received", ["TEST_STATUS"] = "Done",
                // 2026-09-28: creation stamps (reg.created_date / reg_selected_services.created_date) — added 4 h after the registration.
                ["REG_CREATED"] = new DateTime(2026, 8, 15, 9, 0, 0), ["TEST_CREATED"] = new DateTime(2026, 8, 15, 13, 0, 0),
            }),
        };
        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, CancellationToken ct) => Task.FromResult(Rows());
        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, OracleDateWindow window, CancellationToken ct) => Task.FromResult(Rows());
    }

    private sealed class EmptyOracleReader : IOracleReader
    {
        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OracleRow>>(Array.Empty<OracleRow>());

        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, OracleDateWindow window, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OracleRow>>(Array.Empty<OracleRow>());
    }
}
