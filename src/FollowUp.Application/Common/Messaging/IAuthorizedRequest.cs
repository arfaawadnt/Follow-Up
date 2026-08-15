namespace FollowUp.Application.Common.Messaging;

/// <summary>
/// Opt-in privilege requirement for a command/query. The <c>AuthorizationBehavior</c> enforces that the
/// current user holds at least one of <see cref="RequiredPrivileges"/> (SRS Authorization Layer 2). This is
/// backend authorization inside the application pipeline — separate from, and in addition to, the API-layer
/// default-deny route policy. Record-level org-scope/ownership checks happen inside the handler.
/// </summary>
public interface IAuthorizedRequest
{
    /// <summary>The caller must hold at least one of these privileges. Empty means "authenticated only".</summary>
    IReadOnlyCollection<string> RequiredPrivileges { get; }
}
