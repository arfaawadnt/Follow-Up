using FollowUp.Domain.Identity;

namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Records a failed login attempt durably, in a unit of work independent of the login command's transaction.
/// The login command runs under <c>TransactionBehavior</c> and signals a bad password by throwing, which rolls
/// that transaction back — so the failed-attempt counter (and any resulting lockout) must be persisted here,
/// outside it, or per-account lockout (SRS FR-1 / NFR-SEC-4) never takes effect. Finding IDN-1.
/// </summary>
public interface IFailedLoginRecorder
{
    /// <summary>Increments the user's failed-login counter (and locks the account past the threshold) durably.</summary>
    Task RecordAsync(AppUserId userId, CancellationToken ct);
}
