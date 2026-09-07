using FluentAssertions;
using FollowUp.Application.Features.Laboratories.Contracts;
using FollowUp.Domain.Laboratories;
using FollowUp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding M-6 / LAB-005: MappingCode mirrors the real lab code for Oracle-synced labs, so it must be withheld
/// from a caller lacking ShowEncryptedLabs on an encrypted lab — otherwise it leaks the code that DisplayCode masks.
/// </summary>
[Collection("integration")]
public sealed class EncryptedMappingCodeTests
{
    private readonly IntegrationFixture _fx;
    public EncryptedMappingCodeTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Mapping_code_is_withheld_from_a_non_privileged_caller_on_an_encrypted_lab()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        Guid labId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var lab = Laboratory.Register(LabCode.Create("MGL-M601"), "Enc Lab", "A");
            lab.ApplyOracleMaster("Enc Lab", null, null, "Cairo", null, null, null, null); // sets MappingCode = code
            lab.SetEncrypted(true);
            db.Laboratories.Add(lab);
            await db.SaveChangesAsync();
            labId = lab.Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var q = scope.ServiceProvider.GetRequiredService<ILaboratoryQueries>();

            var masked = await q.GetByIdAsync(labId, canSeeEncrypted: false, canSeeLocation: false, CancellationToken.None);
            masked!.MappingCode.Should().BeNull("the real code must not leak via MappingCode for a non-privileged caller");

            var full = await q.GetByIdAsync(labId, canSeeEncrypted: true, canSeeLocation: false, CancellationToken.None);
            full!.MappingCode.Should().Be("MGL-M601", "a privileged caller still sees the mapping code");
        }
    }
}
