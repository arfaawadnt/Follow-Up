namespace FollowUp.Application.Common.Abstractions;

/// <summary>Stores uploaded binary assets on the configured uploads volume (SRS FR-3). Returns the stored path.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct);
}
