namespace FollowUp.Application.Common.Models;

/// <summary>A reference to a visit attachment shown on the board/transfer/check-in/sample-tracking rows. The
/// bytes are fetched separately through the authenticated serve endpoint (GET /daily/attachments/{id}).</summary>
public sealed record AttachmentRefDto(Guid Id, string FileName, string ContentType, long SizeBytes);
