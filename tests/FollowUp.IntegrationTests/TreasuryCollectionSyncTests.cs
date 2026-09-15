using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Security;
using FollowUp.Application.Features.Accounting;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Collection → treasury mirroring and per-treasury rights (2026-09-16) through the real read side, runner and database:
/// the daily automation links a cash collection that has no mirror to the treasury serving the lab's branch; the read
/// side shows only the treasuries a role is granted, with the role's rights; the DB refuses the shapes the domain forbids.
/// </summary>
[Collection("integration")]
public sealed class TreasuryCollectionSyncTests
{
    private readonly IntegrationFixture _fx;
    public TreasuryCollectionSyncTests(IntegrationFixture fx) => _fx = fx;

    private static readonly DateOnly D = new(2026, 9, 14);
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [SkippableFact]
    public async Task Automation_links_unmirrored_cash_collections_and_grants_shape_what_a_role_sees()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag();
        Treasury giza, cairo; Laboratory gizaLab; Representative rep; Collection cashColl, bankColl; Role cashier;

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            giza = Treasury.Create($"Giza {tag}", new[] { $"BR-G-{tag}" }); cairo = Treasury.Create($"Cairo {tag}", new[] { $"BR-C-{tag}" });
            db.Treasuries.AddRange(giza, cairo);
            gizaLab = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}", "B");
            gizaLab.PlaceInHierarchy($"BR-G-{tag}", "Giza", null, null);
            db.Laboratories.Add(gizaLab);
            rep = Representative.Register($"Rep {tag}", RepresentativeType.Collector, GoalDuration.Monthly, Money.Zero, Money.Zero); db.Representatives.Add(rep);
            // Written straight to the table (as collections predating the mirroring were): no mirror yet.
            cashColl = Collection.Create(gizaLab.Id, D, CollectionType.Single, new[] { rep.Id }, 1000m, 0m, null, "Cashier", null);
            bankColl = Collection.Create(gizaLab.Id, D, CollectionType.Single, new[] { rep.Id }, 0m, 500m, IbanOption.Iban16, null, null);
            db.Collections.AddRange(cashColl, bankColl);
            cashier = Role.Create($"Cashier {tag}", new[] { Privileges.ViewAccounting }, "en", "light", OrgScope.Global); db.Roles.Add(cashier);
            db.TreasuryGrants.Add(TreasuryGrant.Create(cashier.Id, giza.Id, view: true, validate: true, update: false));
            await db.SaveChangesAsync();
        }

        try
        {
            using (var scope = _fx.Services.CreateScope())
            {
                // The Treasury page action mirrors what the nightly automation would; the nightly pass then finds nothing more.
                var sync = scope.ServiceProvider.GetRequiredService<ICollectionTreasurySync>();
                var r = await sync.RunAsync(CancellationToken.None);
                r.Linked.Should().BeGreaterThanOrEqualTo(1);
                var runner = scope.ServiceProvider.GetRequiredService<IDeductionAutomationRunner>();

                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                var mirrors = await db.TreasuryEntries.AsNoTracking().Where(e => e.TreasuryId == giza.Id).ToListAsync();
                var mirror = mirrors.Should().ContainSingle("the cash collection is mirrored once; the bank-only one never is").Subject;
                mirror.CollectionId.Should().Be(cashColl.Id);
                mirror.Debit.Amount.Should().Be(1000m); mirror.Credit.Amount.Should().Be(0m); mirror.ReasonId.Should().BeNull();
                mirror.ValidationStatus.Should().BeSameAs(TreasuryValidationStatus.Pending);
                mirror.SystemNote.Should().Contain($"Lab {tag}").And.Contain($"Rep {tag}");
                (await db.TreasuryEntries.CountAsync(e => e.TreasuryId == cairo.Id)).Should().Be(0);

                // A second run links nothing more (idempotent).
                (await runner.RunAsync(D, manual: true, CancellationToken.None)).CollectionsLinked.Should().Be(0);

                // ---- Read side: the cashier role sees Giza with Validate but not Update, and never Cairo; admin sees both.
                var queries = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();
                var grants = await db.TreasuryGrants.AsNoTracking().Where(g => g.RoleId == cashier.Id).ToListAsync();
                var cashierAccess = TreasuryAccessMap.FromGrants(grants);
                var visible = await queries.TreasuriesAsync(OrgScope.Global, cashierAccess, CancellationToken.None);
                visible.Should().Contain(t => t.Id == giza.Id.Value && t.CanValidate && !t.CanUpdate);
                visible.Should().NotContain(t => t.Id == cairo.Id.Value);
                var entries = await queries.TreasuryEntriesAsync(D, D, null, OrgScope.Global, cashierAccess, CancellationToken.None);
                var dto = entries.Should().ContainSingle(e => e.CollectionId == cashColl.Id.Value).Subject;
                dto.Origin.Should().Be("AutoCollection"); dto.ValidationStatus.Should().Be("Pending"); dto.ReasonName.Should().Be("Collection"); dto.CollectedCash.Should().Be(1000m);
                (await queries.TreasuryEntriesAsync(D, D, cairo.Id.Value, OrgScope.Global, cashierAccess, CancellationToken.None)).Should().BeEmpty("a filter on an ungranted treasury yields nothing");
                var all = await queries.TreasuriesAsync(OrgScope.Global, TreasuryAccessMap.Administrator(), CancellationToken.None);
                all.Should().Contain(t => t.Id == cairo.Id.Value && t.CanValidate && t.CanUpdate);
                var forRole = await queries.TreasuryGrantsAsync(cashier.Id, CancellationToken.None);
                forRole.Should().Contain(g => g.TreasuryId == giza.Id.Value && g.CanValidate && !g.CanUpdate);
                forRole.Should().Contain(g => g.TreasuryId == cairo.Id.Value && !g.CanView);

                // ---- DB refuses the shapes the domain forbids.
                var mirrorId = mirror.Id.Value;
                var credit = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE treasury_entry SET credit = 10, debit = 0 WHERE id = {mirrorId}");
                (await credit.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "a mirrored collection is a debit");
                var validatedNoBy = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE treasury_entry SET validation_status = 'Validated' WHERE id = {mirrorId}");
                (await validatedNoBy.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "Validated needs who/when");
                var manualNoReason = () => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO treasury_entry (id, treasury_id, date, debit, credit, origin, validation_status, created_at, created_by)
VALUES ({Guid.NewGuid()}, {giza.Id.Value}, {D}, 5, 0, 'Manual', 'NotRequired', now(), 'test')");
                (await manualNoReason.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "a manual row needs a reason");
                var dupMirror = () => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO treasury_entry (id, treasury_id, date, debit, credit, origin, validation_status, collection_id, created_at, created_by)
VALUES ({Guid.NewGuid()}, {giza.Id.Value}, {D}, 5, 0, 'AutoCollection', 'Pending', {cashColl.Id.Value}, now(), 'test')");
                (await dupMirror.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23505", "one mirror per collection");
                var dupGrant = () => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO treasury_grant (id, role_id, treasury_id, can_view, can_validate, can_update, created_at, created_by)
VALUES ({Guid.NewGuid()}, {cashier.Id.Value}, {giza.Id.Value}, true, false, false, now(), 'test')");
                (await dupGrant.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23505", "one grant row per role and treasury");
            }
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM treasury_entry WHERE treasury_id IN ({giza.Id.Value}, {cairo.Id.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM collection WHERE laboratory_id = {gizaLab.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM treasury_grant WHERE role_id = {cashier.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM role WHERE id = {cashier.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM treasury WHERE id IN ({giza.Id.Value}, {cairo.Id.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id = {gizaLab.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM representative WHERE id = {rep.Id.Value}");
        }
    }
}
