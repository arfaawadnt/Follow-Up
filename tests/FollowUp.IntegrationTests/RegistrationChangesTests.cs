using FluentAssertions;
using FollowUp.Application;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Features.EmailReports;
using FollowUp.Application.Features.RegistrationChanges;
using FollowUp.Domain.Emailing;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Integration;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// 2026-09-30: the RegLog sync stores REG_LOG rows joined to their registration (window on the modification date, an empty
/// read never wipes the window), the query filters by registration-date and/or modification-date range, and the daily
/// email can attach the Registration Changes section (Excel + PDF). Dates are relative to today so the email window
/// (ending yesterday) always covers the seeded edits.
/// </summary>
[Collection("integration")]
public sealed class RegistrationChangesTests
{
    private readonly IntegrationFixture _fx;
    public RegistrationChangesTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Sync_query_and_email_section()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        DateOnly today;
        using (var scope = _fx.Services.CreateScope()) today = scope.ServiceProvider.GetRequiredService<IClock>().CairoToday;
        var modFrom = today.AddDays(-3); var modTo = today.AddDays(-2); var regDay = modFrom.AddDays(-1);
        var reader = new RegLogReader(regDay, modFrom, modTo);

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            if (!await db.OracleConfigs.AnyAsync())
            { scope.ServiceProvider.GetRequiredService<IOracleConfigRepository>().Add(OracleConfig.Create(enabled: true, intervalHours: 24)); await db.SaveChangesAsync(); }
            // A stale row inside the window (replaced by the sync) and one outside it (kept).
            db.RegistrationChanges.AddRange(
                RegistrationChange.Create(1, 10, "OLD-IN", "P", null, null, null, null, "Patient Name", "a", "b", "u", modFrom.ToDateTime(new TimeOnly(8, 0))),
                RegistrationChange.Create(2, 11, "OLD-OUT", "P", null, null, null, null, "Patient Name", "a", "b", "u", today.AddDays(-10).ToDateTime(new TimeOnly(8, 0))));
            await db.SaveChangesAsync();
        }

        // The sync replaces the window with the fake Oracle rows.
        using (var scope = _fx.Services.CreateScope())
        {
            var runner = ActivatorUtilities.CreateInstance<OracleSyncRunner>(scope.ServiceProvider, (IOracleReader)reader);
            var result = await runner.RunRegistrationChangesAsync(modFrom, modTo, manual: true, CancellationToken.None);
            result.Status.Should().Be("ok"); result.StatsUpserted.Should().Be(2, "the duplicate TRANS_ID is skipped");
            var empty = await ActivatorUtilities.CreateInstance<OracleSyncRunner>(scope.ServiceProvider, (IOracleReader)new EmptyReader())
                .RunRegistrationChangesAsync(modFrom, modTo, manual: true, CancellationToken.None);
            empty.Status.Should().Be("no-rows");
        }
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            (await db.RegistrationChanges.CountAsync()).Should().Be(3, "2 synced + the row outside the window");
            (await db.RegistrationChanges.AnyAsync(c => c.AccNo == "OLD-IN")).Should().BeFalse();
            var q = scope.ServiceProvider.GetRequiredService<IRegistrationChangeQueries>();
            var byMod = await q.ListAsync(null, null, modFrom, modTo, OrgScope.Global, CancellationToken.None);
            byMod.Should().HaveCount(2).And.BeInDescendingOrder(c => c.ModifiedAt);
            var name = byMod.Single(c => c.TransId == 13919);
            name.AccNo.Should().Be("91967763"); name.PatientName.Should().Be("عبد الرحيم رجب عبد الرحيم"); name.Column.Should().Be("Patient Name");
            name.OldValue.Should().Be(@"عبد الرحيم رجب عبد\ الرحيم"); name.ModifiedBy.Should().Be("Mohamed Samir");
            name.RegDate.Should().Be(regDay); name.DelayMinutes.Should().Be(1590, "modified 26 h 30 min after the registration was created");
            name.LabCode.Should().Be("MGL-X"); name.LabName.Should().BeNull("the lab is not in the local catalogue");
            (await q.ListAsync(regDay, regDay, null, null, OrgScope.Global, CancellationToken.None)).Should().HaveCount(1, "only one change belongs to a registration of that day");
            (await q.ListAsync(regDay, regDay, modFrom, modTo, OrgScope.Global, CancellationToken.None)).Should().HaveCount(1);
            var scoped = OrgScope.Create(new[] { "*" }, new[] { "Nowhere" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
            (await q.ListAsync(null, null, modFrom, modTo, scoped, CancellationToken.None)).Should().BeEmpty("no lab of that scope; 'No lab' rows are global-only");
        }

        // The daily email can carry the section: one subscription with only Registration Changes → xlsx + pdf attached.
        var sender = new CapturingEmailSender();
        await using var services = BuildServices(sender);
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var sub = StatsEmailSubscription.Create($"Reg changes {Guid.NewGuid():N}", OrgScope.Global);
            sub.SetReports(lab: false, test: false, area: false, noLab: false, regChanges: true);
            sub.SetRecipients(Array.Empty<Guid>(), new[] { "auditor@example.test" });
            sub.SetSchedule(6, 0, 7); // window = the 7 days ending yesterday → covers modFrom..modTo
            db.StatsEmailSubscriptions.Add(sub); await db.SaveChangesAsync();
            var runner = scope.ServiceProvider.GetRequiredService<IStatsEmailRunner>();
            var run = await runner.RunAsync(sub.Id, CancellationToken.None);
            run.Status.Should().StartWith("sent=1");
            sender.Sent.Should().ContainSingle().Which.Html.Should().Contain("Registration Changes").And.Contain("91967763");
            sender.Sent.Single().Attachments.Should().Be(2, "Registration-Changes xlsx + pdf");
        }
    }

    /// <summary>The real service graph with the capturing sender in place of SMTP (same recipe as StatsEmailBranchFilterTests).</summary>
    private static ServiceProvider BuildServices(CapturingEmailSender sender)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:FollowUp"] = Environment.GetEnvironmentVariable("FOLLOWUP_DB"),
            ["Auth:SigningSecret"] = "integration-test-signing-secret-value-0123456789",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddApplication();
        services.AddInfrastructure(config);
        services.AddScoped<ICurrentUser>(_ => IntegrationFixture.TestCurrentUser);
        services.AddScoped<IEmailSender>(_ => sender);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public readonly List<(string To, string Subject, string Html, int Attachments)> Sent = new();
        public Task SendAsync(string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment> attachments, CancellationToken ct)
        { Sent.Add((toEmail, subject, htmlBody, attachments.Count)); return Task.CompletedTask; }
        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct) => SendAsync(toEmail, subject, htmlBody, Array.Empty<EmailAttachment>(), ct);
    }

    /// <summary>Two REG_LOG rows (+ a duplicate TRANS_ID) modified on the two window days; the first registration was created the day before.</summary>
    private sealed class RegLogReader : IOracleReader
    {
        private readonly IReadOnlyList<OracleRow> _rows;
        public RegLogReader(DateOnly regDay, DateOnly modFrom, DateOnly modTo)
        {
            _rows = new[]
            {
                Row(13919, 20000000908757, "91967763", "عبد الرحيم رجب عبد الرحيم", regDay.ToDateTime(new TimeOnly(10, 0)), regDay.ToDateTime(TimeOnly.MinValue), "1", "MGL-X",
                    "Patient Name", @"عبد الرحيم رجب عبد\ الرحيم", "عبد الرحيم رجب عبد الرحيم", "Mohamed Samir", modFrom.ToDateTime(new TimeOnly(12, 30))),
                Row(13920, 20000000908756, "91967764", "شعبان عبد الناصر عبود", modFrom.ToDateTime(new TimeOnly(9, 0)), modFrom.ToDateTime(TimeOnly.MinValue), "1", null,
                    "Doctor", "Dr A", "Dr B", "Mohamed Samir", modTo.ToDateTime(new TimeOnly(9, 5))),
                Row(13920, 20000000908756, "91967764", "dup", null, null, null, null, "Doctor", null, null, "x", modTo.ToDateTime(new TimeOnly(9, 6))), // duplicate TRANS_ID → skipped
            };
        }

        private static OracleRow Row(long trans, long reg, string acc, string patient, DateTime? created, DateTime? regDt, string? branch, string? lab,
            string column, string? oldV, string? newV, string by, DateTime modified) => new(new Dictionary<string, object?>
            {
                ["TRANS_ID"] = trans,
                ["REG_KEY"] = reg,
                ["ACC_NO"] = acc,
                ["PATIENT_NAME"] = patient,
                ["REG_CREATED"] = created,
                ["REG_DT"] = regDt,
                ["REG_BRANCH_CODE"] = branch,
                ["LAB_CODE"] = lab,
                ["REG_COLUMN"] = column,
                ["OLD_VALUE"] = oldV,
                ["NEW_VALUE"] = newV,
                ["MODIFIED_BY"] = by,
                ["MODIFIED_DATE"] = modified,
            });
        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, CancellationToken ct) => Task.FromResult(_rows);
        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, OracleDateWindow window, CancellationToken ct) => Task.FromResult(_rows);
    }

    private sealed class EmptyReader : IOracleReader
    {
        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, CancellationToken ct) => Task.FromResult<IReadOnlyList<OracleRow>>(Array.Empty<OracleRow>());
        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, OracleDateWindow window, CancellationToken ct) => Task.FromResult<IReadOnlyList<OracleRow>>(Array.Empty<OracleRow>());
    }
}
