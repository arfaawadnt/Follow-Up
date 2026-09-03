using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Complaints;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Gateways;

/// <summary>
/// Computes the tamper-evident content hash + version of a signable record (SRS FR-19). Currently the
/// <c>complaint</c> module is signable (the reference build's scope). The hash covers the fields whose change
/// should invalidate a signature.
/// </summary>
public sealed class RecordHasher : IRecordHasher
{
    private readonly FollowUpDbContext _db;
    public RecordHasher(FollowUpDbContext db) => _db = db;

    public async Task<(string ContentHash, uint Version)?> ComputeAsync(string module, string recordId, CancellationToken ct)
    {
        if (!string.Equals(module, "complaint", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!Guid.TryParse(recordId, out var id)) return null;

        var c = await _db.Complaints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == new ComplaintId(id), ct);
        if (c is null) return null;

        // Canonical content: EVERY material field whose change must invalidate a bound signature (SRS FR-19).
        // Serialized as JSON so field boundaries are unambiguous — the previous "|"-joined string both omitted
        // the investigation/outcome/resolution fields (SIG-1) and could collide across free-text values (SIG-7,
        // e.g. AssignedTeam="Ops",Details="x|y" vs AssignedTeam="Ops|x",Details="y"). Changing this formula
        // invalidates signatures made under the old one (hard-cutover decision 2026-08-27): they verify as
        // "record changed" and must be re-signed. See docs/adr/0008-esign-hash-hard-cutover.md.
        var canonical = JsonSerializer.Serialize(new
        {
            c.Number,
            LaboratoryId = c.LaboratoryId.Value,
            c.Category,
            c.ViaChannel,
            c.AssignedTeam,
            c.Details,
            Status = c.Status.Name,
            Stage = c.Stage.Name,
            c.IsValid,
            c.ValidityNotes,
            c.InvestigationNotes,
            c.OutcomeType,
            c.OutcomeSummary,
            c.ResolutionSummary,
            c.RepresentativeId,
            c.ReceivedAt,
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        // The complaint's monotonic ContentVersion (bumped by every material mutator) is the real version — a
        // strictly increasing counter, so an edit-and-revert (A→B→A) cannot resurrect an earlier signature the
        // way the previous hash-derived "version" did (SIG-4). The hash stays the change discriminator; the
        // version adds continuity. StillValidFor requires BOTH to match.
        return (hash, c.ContentVersion);
    }
}

/// <summary>
/// Enforces the e-signature gate (SRS FR-11/FR-19). Enforcement is a per-module setting
/// (<c>esign.enforce.&lt;module&gt;</c>); a signature is valid when the latest bound signature still matches the
/// record's current hash/version (via <see cref="IRecordHasher"/>).
/// </summary>
public sealed class ElectronicSignatureGate : IElectronicSignatureGate
{
    private readonly IAppSettingRepository _settings;
    private readonly IElectronicSignatureRepository _signatures;
    private readonly IRecordHasher _recordHasher;

    public ElectronicSignatureGate(IAppSettingRepository settings, IElectronicSignatureRepository signatures, IRecordHasher recordHasher)
    {
        _settings = settings;
        _signatures = signatures;
        _recordHasher = recordHasher;
    }

    public async Task<bool> IsEnforcedAsync(string module, CancellationToken ct)
    {
        var setting = await _settings.GetAsync($"esign.enforce.{module}", ct);
        return bool.TryParse(setting?.Value, out var enforced) && enforced;
    }

    public async Task<bool> HasValidSignatureAsync(string module, string recordId, CancellationToken ct)
    {
        var signature = await _signatures.GetLatestAsync(module, recordId, ct);
        if (signature is null) return false;
        var computed = await _recordHasher.ComputeAsync(module, recordId, ct);
        return computed is { } c && signature.StillValidFor(c.ContentHash, c.Version);
    }
}
