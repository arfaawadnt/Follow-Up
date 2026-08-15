using FollowUp.Domain.Signatures;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="ElectronicSignature"/> (write side; ADR-0005).</summary>
public interface IElectronicSignatureRepository
{
    void Add(ElectronicSignature signature);

    /// <summary>The most recent signature bound to a record, if any (for verification/resolve gate).</summary>
    Task<ElectronicSignature?> GetLatestAsync(string module, string recordId, CancellationToken ct);
}
