using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Models;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Operations;
using FluentValidation;
using ValidationException = FollowUp.Application.Common.Exceptions.ValidationException;

namespace FollowUp.Application.Features.DailyBoard.Attachments;

// ---- Upload (pending, unbound) ----

/// <summary>
/// Uploads one visit-attachment document (PDF/JPEG/PNG, ≤10 MB), content-sniffed from magic bytes (never the
/// client's claim), stored on the private attachments volume as a pending (unbound) row. The returned id is later
/// bound to a visit by the check-in / manual-record command.
/// </summary>
public sealed record UploadVisitAttachmentCommand(byte[] Content, string FileName) : ICommand<AttachmentRefDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddDailyFollowup };
}

public sealed class UploadVisitAttachmentValidator : AbstractValidator<UploadVisitAttachmentCommand>
{
    public UploadVisitAttachmentValidator()
    {
        RuleFor(x => x.FileName).NotEmpty();
    }
}

public sealed class UploadVisitAttachmentHandler : ICommandHandler<UploadVisitAttachmentCommand, AttachmentRefDto>
{
    private const int MaxBytes = 10 * 1024 * 1024; // 10 MB
    private readonly IAttachmentStorage _storage;
    private readonly IVisitAttachmentRepository _repository;

    public UploadVisitAttachmentHandler(IAttachmentStorage storage, IVisitAttachmentRepository repository)
    {
        _storage = storage;
        _repository = repository;
    }

    public async Task<AttachmentRefDto> Handle(UploadVisitAttachmentCommand request, CancellationToken ct)
    {
        var content = request.Content;
        if (content is null || content.Length == 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = new[] { "The file is empty." } });
        if (content.Length > MaxBytes)
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = new[] { "The file exceeds the 10 MB limit." } });

        var (extension, contentType) = Sniff(content)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["file"] = new[] { "Only PDF, JPEG and PNG documents are accepted." } });

        var stored = await _storage.SaveAsync(content, extension, ct);
        var attachment = VisitAttachment.CreatePending(stored, SafeName(request.FileName, extension), contentType, content.Length);
        _repository.Add(attachment);
        return new AttachmentRefDto(attachment.Id.Value, attachment.FileName, attachment.ContentType, attachment.SizeBytes);
    }

    // Type from the leading bytes only (NFR-SEC-6): PDF (%PDF), JPEG, PNG.
    private static (string Extension, string ContentType)? Sniff(byte[] b) => b switch
    {
    [0x25, 0x50, 0x44, 0x46, ..] => (".pdf", "application/pdf"),
    [0xFF, 0xD8, 0xFF, ..] => (".jpg", "image/jpeg"),
    [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => (".png", "image/png"),
        _ => null,
    };

    // Keep only the base file name (no directory components), cap length, and normalise the extension to the
    // sniffed one so the display name can't misrepresent the real type.
    private static string SafeName(string fileName, string extension)
    {
        var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        baseName = new string(baseName.Where(c => !System.IO.Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        if (string.IsNullOrEmpty(baseName)) baseName = "document";
        if (baseName.Length > 120) baseName = baseName[..120];
        return baseName + extension;
    }
}

// ---- Serve (authenticated + org-scoped) ----

public sealed record VisitAttachmentContent(string FileName, string ContentType, byte[] Content);

/// <summary>Read-side access to a bound visit attachment's bytes, filtered to the caller's org-scope over the
/// owning lab. Returns null when the attachment is pending/missing or outside scope.</summary>
public interface IVisitAttachmentQueries
{
    Task<VisitAttachmentContent?> GetForViewingAsync(Guid id, OrgScope scope, CancellationToken ct);
}

/// <summary>Streams a bound visit attachment's bytes, gated by any of the pages' view privileges and the caller's
/// org-scope over the owning lab. Pending (unbound), missing, or out-of-scope attachments return not-found.</summary>
public sealed record GetVisitAttachmentQuery(Guid Id) : IQuery<VisitAttachmentContent>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[]
    {
        Privileges.ViewDailyFollowup, Privileges.ViewTransfers, Privileges.ConfirmTransfers,
        Privileges.ManageTransfers, Privileges.SampleTracking,
    };
}

public sealed class GetVisitAttachmentHandler : IQueryHandler<GetVisitAttachmentQuery, VisitAttachmentContent>
{
    private readonly IVisitAttachmentQueries _queries;
    private readonly ICurrentUser _user;

    public GetVisitAttachmentHandler(IVisitAttachmentQueries queries, ICurrentUser user)
    {
        _queries = queries; _user = user;
    }

    public async Task<VisitAttachmentContent> Handle(GetVisitAttachmentQuery request, CancellationToken ct) =>
        await _queries.GetForViewingAsync(request.Id, _user.Scope, ct)
            ?? throw new NotFoundException("Attachment", request.Id);
}
