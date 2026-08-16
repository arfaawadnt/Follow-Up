using FollowUp.Application.Common.Abstractions;
using Microsoft.Extensions.Configuration;

namespace FollowUp.Infrastructure.Gateways;

/// <summary>
/// Stores uploads on a local volume (SRS FR-3 / deployment "uploads volume"). Files get a GUID name so a
/// hostile client can't control the path or overwrite anything; the path is read from <c>Uploads:Path</c>.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IConfiguration config)
    {
        _root = config["Uploads:Path"] ?? Path.Combine(AppContext.BaseDirectory, "uploads");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(byte[] content, string extension, CancellationToken ct)
    {
        var name = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(_root, name), content, ct);
        return $"/uploads/{name}";
    }
}
