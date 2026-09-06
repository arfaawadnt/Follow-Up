using FollowUp.Domain.Operations;

namespace FollowUp.Application.Common.Abstractions.Persistence;

public interface IVisitAttachmentRepository
{
    void Add(VisitAttachment attachment);
    Task<VisitAttachment?> GetByIdAsync(VisitAttachmentId id, CancellationToken ct);
    Task<IReadOnlyList<VisitAttachment>> GetByIdsAsync(IReadOnlyCollection<VisitAttachmentId> ids, CancellationToken ct);
}
