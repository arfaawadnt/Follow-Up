using System.Security.Cryptography;
using System.Text;
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

        // Canonical content: a change to any of these should invalidate an existing signature.
        var canonical = string.Join("|",
            c.Number, c.Category, c.ViaChannel, c.AssignedTeam ?? "", c.Details, c.Status.Name, c.Stage.Name);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        // No dedicated row-version column on complaint; a deterministic version derived from the content lets
        // ElectronicSignature.StillValidFor bind to a specific state.
        var version = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)), 0);
        return (hash, version);
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
