using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.Accounting;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// 2026-09-19 accounting updates, proven against the real database and read side:
/// <list type="bullet">
/// <item>the penalty "performed by" pickers — the reps linked to the chosen lab; for a DataEntry penalty the users who did
/// the data entry / review for the lab's area on the date (Sample Lifecycle Tracking), every user when nobody did;</item>
/// <item>a DataEntry penalty stores and resolves its reviewer; the CHECK refuses a reviewer on any other type;</item>
/// <item>a collection's out-source income and reference number round-trip, the statement credits the net share
/// (cash + bank − out-source), and the CHECKs refuse an out-source above the total or a reference without a bank amount.</item>
/// </list>
/// </summary>
[Collection("integration")]
public sealed class PenaltyReviewerOutsourceTests
{
    private readonly IntegrationFixture _fx;
    public PenaltyReviewerOutsourceTests(IntegrationFixture fx) => _fx = fx;

    private static readonly DateOnly D = new(2026, 9, 19);
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [SkippableFact]
    public async Task Pickers_follow_the_lab_and_the_tracking_the_reviewer_round_trips_and_the_statement_credits_the_net_collection()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag(); var area = $"Area {tag}";
        Laboratory lab; Representative responsible, collector; AppUser typist, reviewer, other; Role role;
        Guid penaltyId, collectionId, trackingId;

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            responsible = Representative.Register($"Resp {tag}", RepresentativeType.LabResponsible, GoalDuration.Monthly, Money.Zero, Money.Zero);
            collector = Representative.Register($"Coll {tag}", RepresentativeType.Collector, GoalDuration.Monthly, Money.Zero, Money.Zero);
            db.Representatives.AddRange(responsible, collector);
            lab = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}", "B");
            lab.PlaceInHierarchy("Cairo", "Cairo", null, area);
            lab.AssignResponsible(responsible.Id);
            db.Laboratories.Add(lab);
            role = Role.Create($"Role {tag}", new[] { Privileges.ViewDashboard }, "en", "light", OrgScope.Global); db.Roles.Add(role);
            typist = AppUser.Create($"typist-{tag}", hasher.Hash("pw12345678"), role.Id);
            reviewer = AppUser.Create($"reviewer-{tag}", hasher.Hash("pw12345678"), role.Id);
            other = AppUser.Create($"other-{tag}", hasher.Hash("pw12345678"), role.Id);
            db.Users.AddRange(typist, reviewer, other);
            // The Sample Lifecycle Tracking row of the area for the date: typist entered, reviewer reviewed.
            var tracking = SampleTracking.Open(area, D);
            tracking.RecordDataEntry(12, typist.Username, DateTimeOffset.UtcNow);
            tracking.RecordReview(reviewer.Username, DateTimeOffset.UtcNow);
            db.SampleTracking.Add(tracking); trackingId = tracking.Id.Value;

            var penalty = PenaltyRecord.Create(lab.Id, D, "ACC-DE", "Patient", "T1", "Wrong", 30m, "T2", "Right", 80m, PenaltyUser.DataEntry, typist.Id, null, reviewer.Id);
            db.PenaltyRecords.Add(penalty); penaltyId = penalty.Id.Value;
            var collection = Collection.Create(D, CollectionType.Single, new[] { (responsible.Id, 0m) }, 300m, 200m, IbanOption.Iban16, "Cashier", null, 100m, "TRX-42");
            db.Collections.Add(collection); collectionId = collection.Id.Value;
            await db.SaveChangesAsync();
        }

        try
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var queries = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();
            var ct = CancellationToken.None;

            // Rep picker: narrowed to the reps linked to the lab; every rep without a lab.
            var onLab = await queries.PenaltyActorsAsync(PenaltyUser.Rep, lab.Id.Value, null, null, OrgScope.Global, ct);
            onLab.Should().ContainSingle().Which.Id.Should().Be(responsible.Id.Value);
            var allReps = await queries.PenaltyActorsAsync(PenaltyUser.Rep, null, null, null, OrgScope.Global, ct);
            allReps.Should().Contain(a => a.Id == collector.Id.Value).And.Contain(a => a.Id == responsible.Id.Value);

            // DataEntry pickers: the tracked users of the lab's area on the date, each step its own list…
            var entered = await queries.PenaltyActorsAsync(PenaltyUser.DataEntry, lab.Id.Value, D, PenaltyActorStep.DataEntry, OrgScope.Global, ct);
            entered.Should().ContainSingle().Which.Should().Match<PenaltyActorDto>(a => a.Id == typist.Id.Value && a.Detail!.StartsWith("data entry"));
            var reviewed = await queries.PenaltyActorsAsync(PenaltyUser.DataEntry, lab.Id.Value, D, PenaltyActorStep.Review, OrgScope.Global, ct);
            reviewed.Should().ContainSingle().Which.Should().Match<PenaltyActorDto>(a => a.Id == reviewer.Id.Value && a.Detail!.StartsWith("reviewed"));
            // … and every active user when nobody did the step that day.
            var nobody = await queries.PenaltyActorsAsync(PenaltyUser.DataEntry, lab.Id.Value, D.AddDays(1), PenaltyActorStep.DataEntry, OrgScope.Global, ct);
            nobody.Should().Contain(a => a.Id == other.Id.Value).And.Contain(a => a.Id == typist.Id.Value);
            nobody.Should().OnlyContain(a => a.Detail == null);
            // LabRequest (the report filter): the Lab Responsibles.
            var responsibles = await queries.PenaltyActorsAsync(PenaltyUser.LabRequest, null, null, null, OrgScope.Global, ct);
            responsibles.Should().Contain(a => a.Id == responsible.Id.Value).And.NotContain(a => a.Id == collector.Id.Value);

            // The reviewer round-trips and resolves to a name.
            var row = (await queries.PenaltiesAsync(D, D, lab.Id.Value, null, OrgScope.Global, true, ct)).Should().ContainSingle(p => p.Id == penaltyId).Subject;
            row.PerformedById.Should().Be(typist.Id.Value); row.PerformedByName.Should().Be(typist.Username);
            row.ReviewedById.Should().Be(reviewer.Id.Value); row.ReviewedByName.Should().Be(reviewer.Username);
            row.Penalty.Should().Be(50m, "one record, right − wrong; the doubling is the report's (per person), not the ledger's");

            // The CHECK refuses a reviewer on a non-DataEntry row written past the domain.
            var badReviewer = () => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO penalty_record (id, date, laboratory_id, acc_no, patient_name, wrong_test_code, wrong_test_name, wrong_value,
    right_test_code, right_test_name, right_value, penalty_user, performed_by_user_id, performed_by_rep_id, reviewed_by_user_id, created_at, created_by)
VALUES ({Guid.NewGuid()}, {D}, {lab.Id.Value}, 'X', 'P', 'T1', 'W', 1, 'T2', 'R', 1, 'Technician', {typist.Id.Value}, NULL, {reviewer.Id.Value}, now(), 'test')");
            (await badReviewer.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "ck_penalty_record_reviewed_by");

            // Collection: out-source income and reference round-trip; the statement credits cash + bank − out-source.
            var c = (await queries.CollectionsAsync(D, D, responsible.Id.Value, OrgScope.Global, ct)).Should().ContainSingle(x => x.Id == collectionId).Subject;
            c.OutsourceIncome.Should().Be(100m); c.ReferenceNumber.Should().Be("TRX-42"); c.NetIncome.Should().Be(400m); c.Total.Should().Be(500m);
            var st = await queries.StatementAsync(StatementBy.Responsible, responsible.Id.Value, D, D, OrgScope.Global, ct);
            var credit = st!.Rows.Should().ContainSingle(r => r.Kind == "Collection" && r.SourceId == collectionId).Subject;
            credit.Credit.Should().Be(400m, "500 collected − 100 out-source");
            credit.Notes.Should().Contain("out-source 100.00").And.Contain("ref TRX-42");

            var overTotal = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE collection SET outsource_income = 500.01 WHERE id = {collectionId}");
            (await overTotal.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "ck_collection_outsource_within_total");
            var refWithoutBank = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE collection SET bank = 0, iban = NULL, cash = 500 WHERE id = {collectionId}");
            (await refWithoutBank.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "ck_collection_reference_iff_bank");
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM penalty_record WHERE laboratory_id = {lab.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM treasury_entry WHERE collection_id = {collectionId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM collection WHERE id = {collectionId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM sample_tracking WHERE id = {trackingId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE id IN ({typist.Id.Value}, {reviewer.Id.Value}, {other.Id.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM role WHERE id = {role.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id = {lab.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM representative WHERE id IN ({responsible.Id.Value}, {collector.Id.Value})");
        }
    }
}
