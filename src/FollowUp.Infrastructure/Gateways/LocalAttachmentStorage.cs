using FollowUp.Application.Common.Abstractions;
using Microsoft.Extensions.Configuration;

namespace FollowUp.Infrastructure.Gateways;

/// <summary>
/// Stores visit-attachment bytes on a PRIVATE volume (config key <c>Attachments:Path</c>, default
/// <c>{BaseDirectory}/attachments</c>) — deliberately NOT under <c>Uploads:Path</c>, which is served as public
/// static files. Files get a GUID name; they are read back only by the authenticated serve endpoint.
/// </summary>
public sealed class LocalAttachmentStorage : IAttachmentStorage
{
    private readonly string _root;

    public LocalAttachmentStorage(IConfiguration config)
    {
        _root = config["Attachments:Path"] ?? Path.Combine(AppContext.BaseDirectory, "attachments");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct)
    {
        var name = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(_root, name), content, ct);
        return name;
    }

    public async Task<byte[]?> ReadAsync(string storedName, CancellationToken ct)
    {
        // Only a bare GUID file name is valid — reject anything that could escape the attachments root.
        if (string.IsNullOrWhiteSpace(storedName) || storedName.Contains('/') || storedName.Contains('\\') || storedName.Contains(".."))
            return null;
        var path = Path.Combine(_root, storedName);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }
}
