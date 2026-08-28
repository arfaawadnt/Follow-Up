using FluentAssertions;
using FollowUp.Application.Features.Compensation;
using FollowUp.Application.Features.Complaints.Commands;
using FollowUp.Application.Features.Complaints.Contracts;
using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Features.Marketing;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// CPN-4: the per-lab confidentiality rule (mask ONLY encrypted labs for callers without ShowEncryptedLabs;
/// plain labs always show their real code) was implemented correctly only in LaboratoryQueries and half-copied
/// across the loyalty/board/complaint/marketing projections, which masked plain labs too. These tests exercise
/// each of those read paths through the centralized DisplayCode.For; each fails if the helper drops the
/// isEncrypted arm (the original bug).
/// </summary>
[Collection("integration")]
public sealed class EncryptedCodeReadPathTests
{
    private readonly IntegrationFixture _fx;
    public EncryptedCodeReadPathTests(IntegrationFixture fx) => _fx = fx;

    private async Task<T> Send<T>(IRequest<T> cmd)
    {
        using var scope = _fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(cmd);
    }

    // A plain lab and an encrypted lab, both scheduled every day so board generation yields a visit for each.
    private async Task<(Guid plain, Guid enc)> SeedPlainAndEncryptedLabs(string tag)
    {
        var everyDay = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var plain = await Send(new CreateLaboratoryCommand
        {
            Code = $"MGL-{tag}P", Name = "Plain Lab", Segment = "A", Governorate = "Cairo",
            WorkDays = everyDay, VisitTimes = new[] { "09:00" },
        });
        var enc = await Send(new CreateLaboratoryCommand
        {
            Code = $"MGL-{tag}E", Name = "Secret Lab", Segment = "A", Governorate = "Cairo", IsEncrypted = true,
            WorkDays = everyDay, VisitTimes = new[] { "09:00" },
        });
        return (plain, enc);
    }

    private async Task<Guid> SeedRep(string name, RepresentativeType type)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        var rep = Representative.Register(name, type, GoalDuration.Monthly, new Money(0), new Money(0));
        db.Representatives.Add(rep);
        await db.SaveChangesAsync();
        return rep.Id.Value;
    }

    [SkippableFact]
    public async Task Loyalty_summary_masks_only_the_encrypted_lab_for_an_unprivileged_caller()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        await SeedPlainAndEncryptedLabs("LOY");

        using var scope = _fx.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ICompensationQueries>()
            .GetLoyaltySummaryAsync(OrgScope.Global, canSeeEncrypted: false, default);

        rows.Should().Contain(r => r.Code == "MGL-LOYP", "a plain lab keeps its real code on the loyalty page (CPN-4)");
        rows.Should().Contain(r => r.Code.StartsWith("ENC-"), "the encrypted lab is masked");
        rows.Should().NotContain(r => r.Code == "MGL-LOYE");
    }

    [SkippableFact]
    public async Task Complaint_list_masks_only_the_encrypted_lab_for_an_unprivileged_caller()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        var (plain, enc) = await SeedPlainAndEncryptedLabs("CMP");
        await Send(new LogComplaintCommand { LaboratoryId = plain, Category = "Result Quality", ViaChannel = "Phone", Details = "x" });
        await Send(new LogComplaintCommand { LaboratoryId = enc, Category = "Result Quality", ViaChannel = "Phone", Details = "x" });

        using var scope = _fx.Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IComplaintQueries>()
            .SearchAsync(new ComplaintSearchCriteria(), OrgScope.Global, canSeeEncrypted: false, default);

        page.Items.Should().Contain(i => i.LabDisplayCode == "MGL-CMPP", "a plain lab keeps its real code on the complaints list (CPN-4)");
        page.Items.Should().Contain(i => i.LabDisplayCode.StartsWith("ENC-"), "the encrypted lab is masked");
    }

    [SkippableFact]
    public async Task Marketing_list_masks_only_the_encrypted_lab_for_an_unprivileged_caller()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        var (plain, enc) = await SeedPlainAndEncryptedLabs("MKT");
        var repId = await SeedRep("Marketing Rep", RepresentativeType.Marketing);
        await Send(new ScheduleMarketingVisitCommand { LaboratoryId = plain, RepresentativeId = repId, Purpose = "Routine", ScheduledDate = new DateOnly(2026, 8, 20) });
        await Send(new ScheduleMarketingVisitCommand { LaboratoryId = enc, RepresentativeId = repId, Purpose = "Routine", ScheduledDate = new DateOnly(2026, 8, 20) });

        using var scope = _fx.Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IMarketingQueries>()
            .SearchAsync(new MarketingSearchCriteria(), OrgScope.Global, canSeeEncrypted: false, default);

        page.Items.Should().Contain(i => i.LabDisplayCode == "MGL-MKTP", "a plain lab keeps its real code on the marketing list (CPN-4)");
        page.Items.Should().Contain(i => i.LabDisplayCode.StartsWith("ENC-"), "the encrypted lab is masked");
    }

    [SkippableFact]
    public async Task Daily_board_masks_only_the_encrypted_lab_for_an_unprivileged_caller()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        await SeedPlainAndEncryptedLabs("BRD");
        var day = new DateOnly(2026, 8, 17);

        using (var scope = _fx.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BoardService>().GenerateBoardAsync(day);

        using var qscope = _fx.Services.CreateScope();
        var board = await qscope.ServiceProvider.GetRequiredService<IDailyBoardQueries>()
            .GetBoardAsync(day, day, null, null, OrgScope.Global, canSeeEncrypted: false, default);

        board.Should().Contain(i => i.LabDisplayCode == "MGL-BRDP", "a plain lab keeps its real code on the daily board (CPN-4)");
        board.Should().Contain(i => i.LabDisplayCode.StartsWith("ENC-"), "the encrypted lab is masked");
    }
}
