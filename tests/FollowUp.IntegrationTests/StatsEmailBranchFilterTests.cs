using FluentAssertions;
using FollowUp.Application;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Emailing;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// The scheduled statistics email honours the saved <c>branches</c> filter (the labs' operator-managed serving
/// branch, added 2026-09-15) — proven through the REAL runner and read side against the database, with only the SMTP
/// gateway replaced by a capturing sender. Also proves the payload stays backward compatible: a subscription saved
/// before the filter existed (no <c>branches</c> member) still reports every branch.
/// </summary>
[Collection("integration")]
public sealed class StatsEmailBranchFilterTests
{
    private readonly IntegrationFixture _fx;
    public StatsEmailBranchFilterTests(IntegrationFixture fx) => _fx = fx;

    /// <summary>Captures what the runner would have emailed instead of talking to SMTP.</summary>
    private sealed class CapturingEmailSender : IEmailSender
    {
        public readonly List<(string To, string Subject, string Html, int Attachments)> Sent = new();
        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
        { Sent.Add((toEmail, subject, htmlBody, 0)); return Task.CompletedTask; }
        public Task SendAsync(string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment> attachments, CancellationToken ct)
        { Sent.Add((toEmail, subject, htmlBody, attachments.Count)); return Task.CompletedTask; }
    }

    /// <summary>The fixture's DI graph with the mail gateway swapped for the capturing sender (registered last, so it wins).</summary>
    private static ServiceProvider BuildServices(CapturingEmailSender sender)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FollowUp"] = Environment.GetEnvironmentVariable("FOLLOWUP_DB"),
                ["Auth:SigningSecret"] = "integration-test-signing-secret-value-0123456789",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddApplication();
        services.AddInfrastructure(config);
        services.AddScoped<ICurrentUser>(_ => IntegrationFixture.TestCurrentUser);
        services.AddScoped<IEmailSender>(_ => sender);
        return services.BuildServiceProvider();
    }

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [SkippableFact]
    public async Task Branch_filter_limits_the_lab_report_to_labs_served_by_those_branches()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var sender = new CapturingEmailSender();
        await using var sp = BuildServices(sender);
        var tag = Tag();
        var yesterday = sp.GetRequiredService<IClock>().CairoToday.AddDays(-1); // the runner's 1-day window

        var branchA = $"BR-A-{tag}"; var branchB = $"BR-B-{tag}";
        var labA = Lab($"Lab Alpha {tag}", branchA); var labB = Lab($"Lab Beta {tag}", branchB);
        var subFiltered = Subscription($"Branch report {tag}", $"{{\"branches\":[\"{branchA}\"]}}", tag);
        var subLegacy = Subscription($"Legacy report {tag}", "{\"governorates\":[]}", tag); // predates the branches filter

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            db.Laboratories.AddRange(labA, labB);
            var sA = DailyLabStatistic.For(yesterday, labA.Code.Value.ToUpperInvariant()); sA.Set(3, 30, new Money(300m));
            var sB = DailyLabStatistic.For(yesterday, labB.Code.Value.ToUpperInvariant()); sB.Set(4, 40, new Money(400m));
            db.DailyLabStatistics.AddRange(sA, sB);
            db.StatsEmailSubscriptions.AddRange(subFiltered, subLegacy);
            await db.SaveChangesAsync();
        }

        try
        {
            using (var scope = sp.CreateScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<IStatsEmailRunner>();

                var filtered = await runner.RunAsync(subFiltered.Id, CancellationToken.None);
                filtered.Sent.Should().BeTrue(filtered.Status);
                filtered.Recipients.Should().Be(1);
                var mail = sender.Sent.Should().ContainSingle().Subject;
                mail.Html.Should().Contain(labA.Name, "the lab served by the selected branch is reported");
                mail.Html.Should().NotContain(labB.Name, "a lab served by another branch is filtered out");
                mail.Attachments.Should().Be(1, "the Lab Statistics sheet is attached");

                sender.Sent.Clear();
                var legacy = await runner.RunAsync(subLegacy.Id, CancellationToken.None);
                legacy.Sent.Should().BeTrue(legacy.Status);
                var legacyMail = sender.Sent.Should().ContainSingle().Subject;
                legacyMail.Html.Should().Contain(labA.Name).And.Contain(labB.Name,
                    "a payload saved before the branches filter existed keeps reporting every branch");
            }
        }
        finally
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM daily_lab_statistic WHERE lab_code IN ({labA.Code.Value.ToUpperInvariant()}, {labB.Code.Value.ToUpperInvariant()})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM stats_email_subscription WHERE id IN ({subFiltered.Id.Value}, {subLegacy.Id.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM laboratory WHERE id IN ({labA.Id.Value}, {labB.Id.Value})");
        }
    }

    private static Laboratory Lab(string name, string branch)
    {
        var lab = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), name, "B");
        lab.PlaceInHierarchy(branch, "Cairo", null, null); // serving branch is operator-managed (never from Oracle)
        return lab;
    }

    private static StatsEmailSubscription Subscription(string name, string filtersJson, string tag)
    {
        var sub = StatsEmailSubscription.Create(name, OrgScope.Global);
        sub.SetReports(lab: true, test: false, area: false, noLab: false);
        sub.SetFilters(filtersJson);
        sub.SetRecipients(Array.Empty<Guid>(), new[] { $"branch-{tag}@example.test" });
        return sub;
    }
}
