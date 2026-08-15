using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// The authenticated caller for the current request. Its privileges and six-dimension scope are
/// re-derived from the database on every request (SRS NFR-SEC-2) — never trusted from the token — and are
/// exposed here to the application layer for authorization and scope checks.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    AppUserId UserId { get; }
    string Username { get; }
    RoleId RoleId { get; }

    /// <summary>Effective (expanded) privileges the caller holds.</summary>
    IReadOnlySet<string> Privileges { get; }

    /// <summary>The caller's six-dimension organizational scope.</summary>
    OrgScope Scope { get; }

    /// <summary>Set when the account is linked to a representative — constrains to owned records.</summary>
    RepresentativeId? RepresentativeId { get; }

    string? Ip { get; }
    string? CorrelationId { get; }

    bool Has(string privilege);
}
