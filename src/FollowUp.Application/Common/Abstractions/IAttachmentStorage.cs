namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Stores visit-attachment bytes on a PRIVATE volume (distinct from the public /uploads static path used by lab
/// images). Files are written under a GUID name and read back only through the authenticated, org-scoped serve
/// endpoint — never exposed as a public URL.
/// </summary>
public interface IAttachmentStorage
{
    /// <summary>Persists the bytes and returns the stored GUID file name (e.g. "ab12….pdf"), not a URL.</summary>
    Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct);

    /// <summary>Reads the bytes for a stored name, or null if the file is missing.</summary>
    Task<byte[]?> ReadAsync(string storedName, CancellationToken ct);

    /// <summary>Deletes a stored file if present (best-effort; a missing file is not an error). Used to reclaim
    /// the bytes of abandoned, never-bound uploads (finding OPS-008).</summary>
    Task DeleteAsync(string storedName, CancellationToken ct);
}
