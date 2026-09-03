using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Complaints.Commands;
using FollowUp.Application.Features.Complaints.Contracts;
using FollowUp.Application.Features.Compensation;
using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Features.Signatures;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Org-scope read-isolation regressions (cycle-2 findings CMP-1, CPN-1, BRD-5, SIG-3). Each read must return
/// nothing for a caller whose org scope excludes the record's laboratory — the SRS SCOPE-READ requirement.
/// The scope-parameterised query methods are tested directly with a restricted OrgScope; SIG-3 is tested
/// through its handler because its scope comes from ICurrentUser.
/// </summary>
[Collection("integration")]
public sealed class ScopeReadIsolationTests
{
    private readonly IntegrationFixture _fx;
    public ScopeReadIsolationTests(IntegrationFixture fx) => _fx = fx;

    // Cairo-only vs Giza-only scopes (all other dimensions wildcard). A Cairo lab is in the first, not the second.
    private static readonly OrgScope Cairo = OrgScope.Create(
        new[] { "*" }, new[] { "Cairo" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
    private static readonly OrgScope Giza = OrgScope.Create(
        new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });

    [SkippableFact]
    public async Task Complaint_detail_and_audit_are_scoped_to_the_labs_governorate()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        var labId = await Send(new CreateLaboratoryCommand
        { Code = "MGL-CMP1", Name = "Cairo Lab", Segment = "A", Governorate = "Cairo" });
        await Send(new LogComplaintCommand
        {
            LaboratoryId = labId,
            Category = "Result Quality",
            ViaChannel = "Phone Call",
            Details = "scope isolation probe",
        });

        Guid complaintId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            complaintId = (await db.Complaints.AsNoTracking()
                .FirstAsync(c => c.LaboratoryId == new LaboratoryId(labId))).Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var q = scope.ServiceProvider.GetRequiredService<IComplaintQueries>();

            (await q.GetByIdAsync(complaintId, OrgScope.Global, true, default)).Should().NotBeNull();
            (await q.GetByIdAsync(complaintId, Cairo, true, default)).Should().NotBeNull();
            (await q.GetByIdAsync(complaintId, Giza, true, default))
                .Should().BeNull("a Giza-scoped caller must not read a Cairo lab's complaint");

            (await q.GetAuditAsync(complaintId, Cairo, default)).Should().NotBeEmpty();
            (await q.GetAuditAsync(complaintId, Giza, default))
                .Should().BeEmpty("a Giza-scoped caller must not read a Cairo complaint's audit trail");
        }
    }

    [SkippableFact]
    public async Task Loyalty_ledger_read_is_scoped_to_the_labs_governorate()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        var labId = await Send(new CreateLaboratoryCommand
        { Code = "MGL-CPN1", Name = "Cairo Lab", Segment = "A", Governorate = "Cairo" });

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var ledger = LabLoyaltyLedger.For(new LaboratoryId(labId), YearMonth.From(clock.CairoToday));
            ledger.Record(target: 100, achieved: 80, points: 50, tier: "Silver", when: clock.UtcNow);
            db.LoyaltyLedgers.Add(ledger);
            await db.SaveChangesAsync();
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var q = scope.ServiceProvider.GetRequiredService<ICompensationQueries>();
            (await q.GetLabLedgerAsync(labId, Cairo, default)).Should().NotBeEmpty();
            (await q.GetLabLedgerAsync(labId, Giza, default))
                .Should().BeEmpty("a Giza-scoped caller must not read a Cairo lab's loyalty ledger");
        }
    }

    [SkippableFact]
    public async Task Suggested_sample_count_is_scoped_to_the_labs_governorate()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        var everyDay = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var labId = await Send(new CreateLaboratoryCommand
        {
            Code = "MGL-BRD5", Name = "Cairo Lab", Segment = "A", Governorate = "Cairo",
            WorkDays = everyDay, VisitTimes = new[] { "09:00" },
        });

        Guid visitId;
        using (var scope = _fx.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var clock = sp.GetRequiredService<IClock>();
            var db = sp.GetRequiredService<FollowUpDbContext>();
            (await sp.GetRequiredService<BoardService>().GenerateBoardAsync(clock.CairoToday)).Should().Be(1);
            var visit = await db.DailyVisits.FirstAsync(v => v.LaboratoryId == new LaboratoryId(labId));
            visit.CheckIn(7, "tester", clock.UtcNow);
            await db.SaveChangesAsync();
            visitId = visit.Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var q = scope.ServiceProvider.GetRequiredService<IDailyBoardQueries>();
            (await q.GetSuggestedSampleCountAsync(visitId, Cairo, default)).Should().Be(7);
            (await q.GetSuggestedSampleCountAsync(visitId, Giza, default))
                .Should().BeNull("a Giza-scoped caller must not read a Cairo lab's suggested sample count");
        }
    }

    [SkippableFact]
    public async Task Signature_verify_is_refused_outside_the_records_scope()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        var labId = await Send(new CreateLaboratoryCommand
        { Code = "MGL-SIG3", Name = "Cairo Lab", Segment = "A", Governorate = "Cairo" });
        await Send(new LogComplaintCommand
        { LaboratoryId = labId, Category = "Result Quality", ViaChannel = "Email", Details = "sig scope probe" });

        Guid complaintId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            complaintId = (await db.Complaints.AsNoTracking()
                .FirstAsync(c => c.LaboratoryId == new LaboratoryId(labId))).Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var query = new VerifySignatureQuery("complaint", complaintId.ToString());

            // In scope: no signature exists yet, so verification returns "not signed" without throwing.
            var inScope = new VerifySignatureHandler(
                sp.GetRequiredService<IElectronicSignatureRepository>(), sp.GetRequiredService<IRecordHasher>(),
                new ScopedUser(Cairo), sp.GetRequiredService<IComplaintRepository>(), sp.GetRequiredService<ILaboratoryRepository>());
            (await inScope.Handle(query, default)).Signed.Should().BeFalse();

            // Out of scope: the record's lab is not visible, so verification is refused (no disclosure).
            var outOfScope = new VerifySignatureHandler(
                sp.GetRequiredService<IElectronicSignatureRepository>(), sp.GetRequiredService<IRecordHasher>(),
                new ScopedUser(Giza), sp.GetRequiredService<IComplaintRepository>(), sp.GetRequiredService<ILaboratoryRepository>());
            await FluentActions.Awaiting(() => outOfScope.Handle(query, default))
                .Should().ThrowAsync<ForbiddenException>();
        }
    }

    [SkippableFact]
    public async Task Commission_read_is_scoped_to_the_reps_attribution()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync(); // NB: ResetAsync does not clear representatives — assert on our specific rep.
        int period;
        Guid repId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            period = YearMonth.From(clock.CairoToday).Code;
            var rep = Representative.Register("Cairo Collector", RepresentativeType.Collector,
                GoalDuration.Monthly, new Money(5000m), new Money(100m));
            rep.AssignScope(branch: null, governorate: "Cairo", area: null, city: null);
            db.Representatives.Add(rep);
            await db.SaveChangesAsync();
            repId = rep.Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var q = scope.ServiceProvider.GetRequiredService<ICompensationQueries>();
            (await q.GetCommissionsAsync(period, Cairo, default))
                .Should().Contain(c => c.RepId == repId, "the Cairo-attributed rep is in a Cairo-scoped read");
            (await q.GetCommissionsAsync(period, Giza, default))
                .Should().NotContain(c => c.RepId == repId,
                    "a Giza-scoped caller must not see a Cairo-attributed rep's commission/salary");
        }
    }

    private async Task<Guid> Send<T>(IRequest<T> cmd)
    {
        using var scope = _fx.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var result = await mediator.Send(cmd);
        return result switch
        {
            Guid g => g,
            string s => Guid.TryParse(s, out var g2) ? g2 : Guid.Empty, // LogComplaint returns a reference, not a Guid
            _ => Guid.Empty,
        };
    }

    /// <summary>A current user with a fixed org scope, for exercising handler-level scope enforcement.</summary>
    private sealed class ScopedUser : ICurrentUser
    {
        public ScopedUser(OrgScope scope) => Scope = scope;
        public bool IsAuthenticated => true;
        public AppUserId UserId { get; } = AppUserId.New();
        public string Username => "scoped-tester";
        public RoleId RoleId { get; } = RoleId.New();
        public UserSessionId? SessionId => null;
        public IReadOnlySet<string> Privileges { get; } = new HashSet<string>(Domain.Identity.Privileges.All);
        public OrgScope Scope { get; }
        public RepresentativeId? RepresentativeId => null;
        public string? Ip => "127.0.0.1";
        public string? CorrelationId => "scope-itest";
        public bool Has(string privilege) => true;
    }
}
