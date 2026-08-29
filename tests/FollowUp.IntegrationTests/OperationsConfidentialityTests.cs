using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Application.Features.LabCheckIn;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Features.Representatives.CreateRepresentative;
using FollowUp.Application.Features.Transfers;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// The operations read models (daily board, transfers, lab check-in) must honour per-lab confidentiality
/// (BR-7) in the SHAPE of their payload, not merely in the masked display code: an encrypted lab's real code
/// must not reach a caller without <c>ShowEncryptedLabs</c> through ANY field of the DTO, while a plain lab
/// must still show its real code (not the mask-everything alias). Guards the raw-<c>LabCode</c> leak these
/// projections carried alongside the masked <c>LabDisplayCode</c>.
/// </summary>
[Collection("integration")]
public sealed class OperationsConfidentialityTests
{
    private const string EncryptedCode = "SEC-9001";
    private const string PlainCode = "PLAIN-7";

    private readonly IntegrationFixture _fx;
    public OperationsConfidentialityTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Board_masks_an_encrypted_labs_real_code_in_every_field_but_keeps_plain_lab_codes()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var (encId, plainId, today) = await SeedBoardAsync();

        using var read = _fx.Services.CreateScope();
        var queries = read.ServiceProvider.GetRequiredService<IDailyBoardQueries>();

        // A caller WITHOUT ShowEncryptedLabs.
        var board = await queries.GetBoardAsync(today, today, null, null, OrgScope.Global, canSeeEncrypted: false, default);

        var enc = board.Single(b => b.LaboratoryId == encId);
        var plain = board.Single(b => b.LaboratoryId == plainId);
        AssertPerLabMasking(enc.LabDisplayCode, StringFields(enc), plain.LabDisplayCode);
    }

    [SkippableFact]
    public async Task Transfers_mask_an_encrypted_labs_real_code_in_every_field_but_keep_plain_lab_codes()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var (encId, plainId, today) = await SeedBoardAsync();

        // Check both visits in (Pending -> Visited) so they become transferable.
        await MutateTodaysVisitsAsync(today, (v, _) => v.CheckIn(5, "tester", _.UtcNow));

        using var read = _fx.Services.CreateScope();
        var queries = read.ServiceProvider.GetRequiredService<ITransferQueries>();
        var rows = await queries.GetTransferableAsync(today, today, OrgScope.Global, canSeeEncrypted: false, default);

        var enc = rows.Single(r => r.LaboratoryId == encId);
        var plain = rows.Single(r => r.LaboratoryId == plainId);
        AssertPerLabMasking(enc.LabDisplayCode, StringFields(enc), plain.LabDisplayCode);
    }

    [SkippableFact]
    public async Task Lab_check_in_masks_an_encrypted_labs_real_code_in_every_field_but_keeps_plain_lab_codes()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var (encId, plainId, today) = await SeedBoardAsync();

        // A transfer rep is required (FK) to confirm the hand-off that puts a visit on the check-in list.
        var transferRepId = new RepresentativeId(await Send(new CreateRepresentativeCommand { FullName = "Transfer Rep", Type = "Transfer" }));

        // Visited -> transfer-confirmed so the visit appears as awaiting receipt.
        await MutateTodaysVisitsAsync(today, (v, clock) =>
        {
            v.CheckIn(5, "tester", clock.UtcNow);
            v.ConfirmTransfer(transferRepId, new TransferDetails("Driver", "0100000000", "ABC-123"), clock.UtcNow);
        });

        using var read = _fx.Services.CreateScope();
        var queries = read.ServiceProvider.GetRequiredService<ILabCheckInQueries>();
        var rows = await queries.GetAwaitingReceiptAsync(today, today, OrgScope.Global, canSeeEncrypted: false, default);

        var enc = rows.Single(r => r.LaboratoryId == encId);
        var plain = rows.Single(r => r.LaboratoryId == plainId);
        AssertPerLabMasking(enc.LabDisplayCode, StringFields(enc), plain.LabDisplayCode);
    }

    // ---- helpers ----

    /// <summary>Encrypted lab -> ENC alias with the real code absent from every field; plain lab -> its real code.</summary>
    private static void AssertPerLabMasking(string encryptedDisplayCode, IEnumerable<string> encryptedFields, string plainDisplayCode)
    {
        encryptedDisplayCode.Should().Be(LabCode.Create(EncryptedCode).ToEncryptedAlias());
        encryptedFields.Should().NotContain(EncryptedCode,
            "an encrypted lab's real code must never reach a caller without ShowEncryptedLabs (any field of the read model)");
        plainDisplayCode.Should().Be(PlainCode);
    }

    private static IEnumerable<string> StringFields<T>(T item) =>
        typeof(T).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.GetValue(item) as string)
            .Where(v => v is not null)
            .Select(v => v!);

    /// <summary>Resets the DB, seeds one encrypted and one plain lab (each scheduled today), and generates the board.</summary>
    private async Task<(Guid EncId, Guid PlainId, DateOnly Today)> SeedBoardAsync()
    {
        await _fx.ResetAsync();
        var everyDay = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var encId = await Send(new CreateLaboratoryCommand
        {
            Code = EncryptedCode, Name = "Confidential Lab", Segment = "A", Governorate = "Cairo",
            IsEncrypted = true, WorkDays = everyDay, VisitTimes = new[] { "09:00" },
        });
        var plainId = await Send(new CreateLaboratoryCommand
        {
            Code = PlainCode, Name = "Open Lab", Segment = "B", Governorate = "Giza",
            WorkDays = everyDay, VisitTimes = new[] { "10:00" },
        });

        DateOnly today;
        using var scope = _fx.Services.CreateScope();
        var sp = scope.ServiceProvider;
        today = sp.GetRequiredService<IClock>().CairoToday;
        (await sp.GetRequiredService<BoardService>().GenerateBoardAsync(today)).Should().Be(2);
        return (encId, plainId, today);
    }

    private async Task MutateTodaysVisitsAsync(DateOnly today, Action<DailyVisit, IClock> mutate)
    {
        using var scope = _fx.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<FollowUpDbContext>();
        var clock = sp.GetRequiredService<IClock>();
        foreach (var visit in await db.DailyVisits.Where(v => v.VisitDate == today).ToListAsync())
            mutate(visit, clock);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> Send(IRequest<Guid> command)
    {
        using var scope = _fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command);
    }
}
