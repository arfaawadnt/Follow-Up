using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
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
/// A penalty's "performed by" person (2026-09-15): the record links to a representative (UserType = Rep) or to a system
/// user (DataEntry / Technician). Proven at the persistence layer and through the real read side: the links round-trip
/// and resolve to names, the DB CHECK refuses a mismatched link that bypasses the domain, and the actor lookup that
/// feeds the "User" picker lists exactly the active people of each kind.
/// </summary>
[Collection("integration")]
public sealed class PenaltyPerformedByTests
{
    private readonly IntegrationFixture _fx;
    public PenaltyPerformedByTests(IntegrationFixture fx) => _fx = fx;

    private static readonly DateOnly D = new(2026, 9, 14);
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [SkippableFact]
    public async Task Performed_by_links_round_trip_and_resolve_to_names_and_the_check_refuses_a_mismatch()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag();
        Laboratory lab; Representative rep; AppUser clerk; Role role;
        Guid repPenaltyId, userPenaltyId;

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            lab = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}", "B"); db.Laboratories.Add(lab);
            rep = Representative.Register($"Rep {tag}", RepresentativeType.Collector, GoalDuration.Monthly, Money.Zero, Money.Zero); db.Representatives.Add(rep);
            role = Role.Create($"Role {tag}", new[] { Privileges.ViewDashboard }, "en", "light", OrgScope.Global); db.Roles.Add(role);
            clerk = AppUser.Create($"clerk-{tag}", hasher.Hash("pw12345678"), role.Id); db.Users.Add(clerk);

            var byRep = PenaltyRecord.Create(lab.Id, D, "ACC-R", "Patient", "T1", "Wrong", 300m, "T2", "Right", 120m, PenaltyUser.Rep, null, rep.Id);
            var byUser = PenaltyRecord.Create(lab.Id, D, "ACC-U", "Patient", "T3", "Wrong", 50m, "T4", "Right", 80m, PenaltyUser.Technician, clerk.Id, null);
            db.PenaltyRecords.AddRange(byRep, byUser);
            await db.SaveChangesAsync();
            repPenaltyId = byRep.Id.Value; userPenaltyId = byUser.Id.Value;
        }

        try
        {
            using (var scope = _fx.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                var queries = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();

                // Round trip through the real read side: type + resolved person name.
                var rows = await queries.PenaltiesAsync(D, D, lab.Id.Value, null, OrgScope.Global, true, CancellationToken.None);
                var r = rows.Should().ContainSingle(x => x.Id == repPenaltyId).Subject;
                r.UserType.Should().Be("Rep");
                r.PerformedById.Should().Be(rep.Id.Value);
                r.PerformedByName.Should().Be(rep.FullName);
                var u = rows.Should().ContainSingle(x => x.Id == userPenaltyId).Subject;
                u.UserType.Should().Be("Technician");
                u.PerformedById.Should().Be(clerk.Id.Value);
                u.PerformedByName.Should().Be(clerk.Username);

                // The CHECK mirrors the domain invariant against writes that bypass it: a DataEntry row naming a rep.
                var act = () => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO penalty_record (id, date, laboratory_id, acc_no, patient_name, wrong_test_code, wrong_test_name, wrong_value,
    right_test_code, right_test_name, right_value, penalty_user, performed_by_user_id, performed_by_rep_id, created_at, created_by)
VALUES ({Guid.NewGuid()}, {D}, {lab.Id.Value}, 'X', 'P', 'T1', 'W', 1, 'T2', 'R', 1, 'DataEntry', NULL, {rep.Id.Value}, now(), 'test')");
                (await act.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "ck_penalty_record_performed_by");

                // Both links at once is refused as well.
                var both = () => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO penalty_record (id, date, laboratory_id, acc_no, patient_name, wrong_test_code, wrong_test_name, wrong_value,
    right_test_code, right_test_name, right_value, penalty_user, performed_by_user_id, performed_by_rep_id, created_at, created_by)
VALUES ({Guid.NewGuid()}, {D}, {lab.Id.Value}, 'X', 'P', 'T1', 'W', 1, 'T2', 'R', 1, 'Rep', {clerk.Id.Value}, {rep.Id.Value}, now(), 'test')");
                (await both.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514");

                // The "User" picker: reps for Rep (active, in scope), system users otherwise.
                var repActors = await queries.PenaltyActorsAsync(PenaltyUser.Rep, OrgScope.Global, CancellationToken.None);
                repActors.Should().Contain(a => a.Id == rep.Id.Value && a.Name == rep.FullName && a.Detail == "Collector");
                repActors.Should().NotContain(a => a.Id == clerk.Id.Value);
                var userActors = await queries.PenaltyActorsAsync(PenaltyUser.Technician, OrgScope.Global, CancellationToken.None);
                userActors.Should().Contain(a => a.Id == clerk.Id.Value && a.Name == clerk.Username);
                userActors.Should().NotContain(a => a.Id == rep.Id.Value);

                // Deactivated people drop out of the picker (existing records keep their link).
                var tracked = await db.Users.SingleAsync(x => x.Id == clerk.Id);
                tracked.Deactivate();
                await db.SaveChangesAsync();
                (await queries.PenaltyActorsAsync(PenaltyUser.DataEntry, OrgScope.Global, CancellationToken.None))
                    .Should().NotContain(a => a.Id == clerk.Id.Value);
            }
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM penalty_record WHERE laboratory_id = {lab.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE id = {clerk.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM role WHERE id = {role.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id = {lab.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM representative WHERE id = {rep.Id.Value}");
        }
    }
}
