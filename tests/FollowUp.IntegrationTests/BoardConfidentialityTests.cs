using System.Reflection;
using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Infrastructure.Jobs;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// The daily board must honour per-lab confidentiality (BR-7) in the SHAPE of its payload, not merely in the
/// masked display code: an encrypted lab's real code must not reach a caller without <c>ShowEncryptedLabs</c>
/// through ANY field of the DTO, while a plain lab must still show its real code (not the mask-everything alias).
/// Mirrors the encrypted-alias read-path tests; guards the raw-<c>LabCode</c> leak the board projection carried
/// alongside the masked <c>LabDisplayCode</c>.
/// </summary>
[Collection("integration")]
public sealed class BoardConfidentialityTests
{
    private readonly IntegrationFixture _fx;
    public BoardConfidentialityTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Board_masks_an_encrypted_labs_real_code_in_every_field_but_keeps_plain_lab_codes()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        const string encryptedCode = "SEC-9001";
        const string plainCode = "PLAIN-7";
        var everyDay = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

        var encId = await Send(new CreateLaboratoryCommand
        {
            Code = encryptedCode, Name = "Confidential Lab", Segment = "A", Governorate = "Cairo",
            IsEncrypted = true, WorkDays = everyDay, VisitTimes = new[] { "09:00" },
        });
        var plainId = await Send(new CreateLaboratoryCommand
        {
            Code = plainCode, Name = "Open Lab", Segment = "B", Governorate = "Giza",
            IsEncrypted = false, WorkDays = everyDay, VisitTimes = new[] { "10:00" },
        });

        DateOnly today;
        using (var scope = _fx.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            today = sp.GetRequiredService<IClock>().CairoToday;
            (await sp.GetRequiredService<BoardService>().GenerateBoardAsync(today)).Should().Be(2);
        }

        using var read = _fx.Services.CreateScope();
        var queries = read.ServiceProvider.GetRequiredService<IDailyBoardQueries>();

        // A caller WITHOUT ShowEncryptedLabs.
        var board = await queries.GetBoardAsync(today, today, null, null, OrgScope.Global, canSeeEncrypted: false, default);

        var enc = board.Single(b => b.LaboratoryId == encId);
        var plain = board.Single(b => b.LaboratoryId == plainId);

        // Encrypted lab -> deterministic ENC alias, and the real code must not survive in ANY string field.
        enc.LabDisplayCode.Should().Be(LabCode.Create(encryptedCode).ToEncryptedAlias());
        StringFieldsOf(enc).Should().NotContain(encryptedCode,
            "an encrypted lab's real code must never reach a caller without ShowEncryptedLabs (any board field)");

        // Plain lab -> real code is still shown (per-lab confidentiality, not mask-everything).
        plain.LabDisplayCode.Should().Be(plainCode);
    }

    private static IEnumerable<string> StringFieldsOf(BoardItemDto item) =>
        typeof(BoardItemDto).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.GetValue(item) as string)
            .Where(v => v is not null)
            .Select(v => v!);

    private async Task<Guid> Send(CreateLaboratoryCommand cmd)
    {
        using var scope = _fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(cmd);
    }
}
