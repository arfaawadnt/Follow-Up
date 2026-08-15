using FluentAssertions;
using FollowUp.Application.Features.Insights;
using FollowUp.Application.Features.Laboratories.Contracts;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Features.Laboratories.GetLaboratories;
using FollowUp.Domain.Identity;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

[Collection("integration")]
public sealed class ReadPathTests
{
    private readonly IntegrationFixture _fx;
    public ReadPathTests(IntegrationFixture fx) => _fx = fx;

    private async Task SeedTwoLabs()
    {
        await _fx.ResetAsync();
        await Send(new CreateLaboratoryCommand { Code = "MGL-A1", Name = "Cairo Lab", Segment = "A", Governorate = "Cairo" });
        await Send(new CreateLaboratoryCommand { Code = "MGL-B1", Name = "Giza Lab", Segment = "B", Governorate = "Giza" });
    }

    [SkippableFact]
    public async Task Search_returns_labs_and_respects_scope_and_segment_in_sql()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await SeedTwoLabs();

        using var scope = _fx.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ILaboratoryQueries>();

        // Global scope → both labs.
        var all = await queries.SearchAsync(new LabSearchCriteria(), OrgScope.Global, canSeeEncrypted: true, default);
        all.Total.Should().Be(2);

        // Governorate-limited scope → only the Cairo lab (scope pushed into SQL).
        var cairoScope = OrgScope.Create(new[] { "*" }, new[] { "Cairo" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
        var cairo = await queries.SearchAsync(new LabSearchCriteria(), cairoScope, true, default);
        cairo.Total.Should().Be(1);
        cairo.Items[0].Name.Should().Be("Cairo Lab");

        // Segment-limited scope (enum IN translation) → only the A-segment lab.
        var segmentA = OrgScope.Create(new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "A" });
        var segA = await queries.SearchAsync(new LabSearchCriteria(), segmentA, true, default);
        segA.Total.Should().Be(1);
        segA.Items[0].Segment.Should().Be("A");
    }

    [SkippableFact]
    public async Task Encrypted_alias_is_applied_when_not_permitted()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await SeedTwoLabs();

        using var scope = _fx.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ILaboratoryQueries>();

        var masked = await queries.SearchAsync(new LabSearchCriteria(), OrgScope.Global, canSeeEncrypted: false, default);
        masked.Items.Should().OnlyContain(i => i.Encrypted && i.DisplayCode.StartsWith("ENC-"));

        var real = await queries.SearchAsync(new LabSearchCriteria(), OrgScope.Global, canSeeEncrypted: true, default);
        real.Items.Should().Contain(i => i.DisplayCode == "MGL-A1");
    }

    [SkippableFact]
    public async Task Dashboard_query_executes_and_counts_active_labs()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await SeedTwoLabs();

        using var scope = _fx.Services.CreateScope();
        var insights = scope.ServiceProvider.GetRequiredService<IInsightsQueries>();

        var dashboard = await insights.GetDashboardAsync(OrgScope.Global, true, new DateOnly(2026, 8, 15), default);

        dashboard.Should().NotBeNull();
        dashboard.OpenComplaints.Should().Be(0);
        dashboard.TodaySchedule.Should().BeEmpty(); // no board generated in this test
    }

    private async Task<Guid> Send(CreateLaboratoryCommand cmd)
    {
        using var scope = _fx.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(cmd);
    }
}
