using FluentAssertions;
using FollowUp.Application.Features.Representatives.Contracts;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding B-5 / LAB-001/LAB-002: the representative directory must enforce the caller's org scope on both
/// the search list and the by-id read, or a scoped user reads other scopes' salary/PII.
/// </summary>
[Collection("integration")]
public sealed class RepresentativeScopeReadTests
{
    private readonly IntegrationFixture _fx;
    public RepresentativeScopeReadTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Rep_directory_is_scoped_on_search_and_by_id()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        Guid cairoRepId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var cairo = Representative.Register("B5 Cairo Rep", RepresentativeType.Collector, GoalDuration.Monthly, Money.Zero, new Money(5000));
            cairo.AssignScope(null, "Cairo", null, null);
            var giza = Representative.Register("B5 Giza Rep", RepresentativeType.Collector, GoalDuration.Monthly, Money.Zero, new Money(4000));
            giza.AssignScope(null, "Giza", null, null);
            db.Representatives.AddRange(cairo, giza);
            await db.SaveChangesAsync();
            cairoRepId = cairo.Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var q = scope.ServiceProvider.GetRequiredService<IRepresentativeQueries>();
            var gizaScope = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });

            var page = await q.SearchAsync(new RepSearchCriteria { Page = 1, PageSize = 200 }, gizaScope, CancellationToken.None);
            page.Items.Should().Contain(r => r.FullName == "B5 Giza Rep");
            page.Items.Should().NotContain(r => r.FullName == "B5 Cairo Rep", "a Cairo rep is outside a Giza scope");

            var byId = await q.GetByIdAsync(cairoRepId, gizaScope, CancellationToken.None);
            byId.Should().BeNull("a Giza-scoped caller must not read a Cairo rep by id");
        }
    }
}
