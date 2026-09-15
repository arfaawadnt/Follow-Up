using FluentAssertions;
using FollowUp.Application.Features.Laboratories.Contracts;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// The lab picker lookup (GET /labs/lookup, 2026-09-15): unpaged, so pickers no longer lose everything past the paged
/// list's 500th row; scoped and code-masked exactly like the directory, so it can never show more than the list does.
/// </summary>
[Collection("integration")]
public sealed class LabLookupQueryTests
{
    private readonly IntegrationFixture _fx;
    public LabLookupQueryTests(IntegrationFixture fx) => _fx = fx;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [SkippableFact]
    public async Task Lookup_returns_every_lab_in_scope_unpaged_with_masked_codes()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag();
        var labs = new List<Laboratory>();
        Laboratory cairoEncrypted, gizaPlain;

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            // More labs than any single "page" a picker used to ask for is unnecessary here — the paged list is not
            // involved at all; what matters is that every in-scope lab comes back in one call.
            cairoEncrypted = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Zeta Lab {tag}", "B");
            cairoEncrypted.PlaceInHierarchy(null, "Cairo", null, null);
            cairoEncrypted.SetEncrypted(true);
            gizaPlain = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Alpha Lab {tag}", "B");
            gizaPlain.PlaceInHierarchy(null, "Giza", null, null);
            labs.AddRange(new[] { cairoEncrypted, gizaPlain });
            db.Laboratories.AddRange(labs);
            await db.SaveChangesAsync();
        }

        try
        {
            using var scope = _fx.Services.CreateScope();
            var queries = scope.ServiceProvider.GetRequiredService<ILaboratoryQueries>();

            // Global scope, cannot see encrypted codes: both labs present, ordered by name, the encrypted code masked.
            var all = await queries.LookupAsync(OrgScope.Global, canSeeEncrypted: false, CancellationToken.None);
            var mine = all.Where(l => l.Name.EndsWith(tag)).ToList();
            mine.Select(l => l.Name).Should().Equal(new[] { $"Alpha Lab {tag}", $"Zeta Lab {tag}" }, "the lookup is ordered by name");
            mine.Single(l => l.Id == gizaPlain.Id.Value).DisplayCode.Should().Be(gizaPlain.Code.Value);
            mine.Single(l => l.Id == cairoEncrypted.Id.Value).DisplayCode.Should().NotBe(cairoEncrypted.Code.Value, "encrypted code is masked without the privilege");

            // With the privilege the real code shows.
            var privileged = await queries.LookupAsync(OrgScope.Global, canSeeEncrypted: true, CancellationToken.None);
            privileged.Single(l => l.Id == cairoEncrypted.Id.Value).DisplayCode.Should().Be(cairoEncrypted.Code.Value);

            // A Giza-scoped caller never sees the Cairo lab — scope is pushed into the query like the directory.
            var giza = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
            var scoped = await queries.LookupAsync(giza, canSeeEncrypted: true, CancellationToken.None);
            scoped.Should().Contain(l => l.Id == gizaPlain.Id.Value);
            scoped.Should().NotContain(l => l.Id == cairoEncrypted.Id.Value);
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            foreach (var l in labs)
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id = {l.Id.Value}");
        }
    }
}
