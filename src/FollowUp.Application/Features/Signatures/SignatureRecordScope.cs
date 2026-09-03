using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Complaints.Commands;

namespace FollowUp.Application.Features.Signatures;

/// <summary>
/// Record-level org-scope check shared by the sign and verify handlers (SRS FR-19: signing and verification
/// are "within organizational scope"). Resolves the signable record to its owning laboratory and enforces the
/// caller's scope via the single-sourced complaint load+authorize. "complaint" is the only signable module
/// today; an unknown module (or an unparseable id) fails closed. Findings SIG-2 (sign) and SIG-3 (verify).
/// </summary>
internal static class SignatureRecordScope
{
    public static async Task EnsureInScopeAsync(string module, string recordId,
        IComplaintRepository complaints, ILaboratoryRepository labs, ICurrentUser user, CancellationToken ct)
    {
        if (string.Equals(module, ComplaintActionSupport.Module, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(recordId, out var complaintId))
        {
            // Throws NotFound if the record is absent, Forbidden if it is outside the caller's scope.
            await ComplaintActionSupport.LoadAuthorizedAsync(complaintId, complaints, labs, user, ct);
            return;
        }
        throw new NotFoundException($"{module} record", recordId);
    }
}
