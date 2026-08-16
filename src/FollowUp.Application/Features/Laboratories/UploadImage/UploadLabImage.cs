using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Laboratories.UploadImage;

/// <summary>
/// Uploads a lab image (SRS FR-3): content-sniffed (JPEG/PNG from magic bytes, not the client's claim),
/// capped at 5 MB, stored under a GUID filename on the uploads volume. Returns the stored path.
/// </summary>
public sealed record UploadLabImageCommand(byte[] Content) : ICommand<string>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddLabs, Privileges.ManageLabs };
}

public sealed class UploadLabImageHandler : ICommandHandler<UploadLabImageCommand, string>
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private readonly IFileStorage _storage;

    public UploadLabImageHandler(IFileStorage storage) => _storage = storage;

    public async Task<string> Handle(UploadLabImageCommand request, CancellationToken ct)
    {
        var content = request.Content;
        if (content is null || content.Length == 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = new[] { "The file is empty." } });
        if (content.Length > MaxBytes)
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = new[] { "The file exceeds the 5 MB limit." } });

        var extension = SniffExtension(content)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["file"] = new[] { "Only JPEG and PNG images are accepted." } });

        return await _storage.SaveAsync(content, extension, ct);
    }

    // Type detected from the leading bytes, never from the supplied name/content-type (NFR-SEC-6).
    private static string? SniffExtension(byte[] b) => b switch
    {
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => ".png",
        _ => null,
    };
}
