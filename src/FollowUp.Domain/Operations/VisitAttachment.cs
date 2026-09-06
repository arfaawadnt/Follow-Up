using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Domain.Operations;

public readonly record struct VisitAttachmentId(Guid Value)
{
    public static VisitAttachmentId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A document (PDF/image) attached to a daily visit when it is recorded. Stored in its own table keyed by the
/// visit's <b>stable</b> id (<see cref="DailyVisit"/>.Id, which <see cref="VisitHistory"/> preserves as
/// OriginalVisitId), so an attachment stays linked after the midnight roll-over archives the visit — no FK to
/// daily_visit (that row is deleted on archival). The bytes live on a private volume (never the public /uploads
/// path); they are served only through an authenticated, org-scoped endpoint. Uploaded first as "pending"
/// (unbound), then bound to a visit when the check-in / manual-record command persists.
/// </summary>
public sealed class VisitAttachment : AggregateRoot<VisitAttachmentId>, IAuditable
{
    private VisitAttachment() { } // EF

    private VisitAttachment(VisitAttachmentId id, string storedName, string fileName, string contentType, long sizeBytes)
        : base(id)
    {
        StoredName = storedName;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
    }

    /// <summary>The owning visit's stable id (DailyVisit.Id / VisitHistory.OriginalVisitId). Null while pending.</summary>
    public Guid? VisitId { get; private set; }
    /// <summary>The owning lab — carried for the scope check on the serve endpoint. Null while pending.</summary>
    public LaboratoryId? LaboratoryId { get; private set; }
    /// <summary>The GUID filename on the private attachments volume (not a public URL).</summary>
    public string StoredName { get; private set; } = null!;
    /// <summary>The original, operator-facing file name (sanitised).</summary>
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    /// <summary>Creates an unbound (pending) attachment right after the bytes are stored.</summary>
    public static VisitAttachment CreatePending(string storedName, string fileName, string contentType, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(storedName)) throw new DomainException("Stored name is required.");
        if (string.IsNullOrWhiteSpace(fileName)) throw new DomainException("File name is required.");
        if (sizeBytes <= 0) throw new DomainException("Attachment is empty.");
        return new VisitAttachment(VisitAttachmentId.New(), storedName.Trim(), fileName.Trim(), contentType, sizeBytes);
    }

    /// <summary>Binds a pending attachment to the visit it was recorded with.</summary>
    public void BindTo(Guid visitId, LaboratoryId laboratoryId)
    {
        VisitId = visitId;
        LaboratoryId = laboratoryId;
    }
}
