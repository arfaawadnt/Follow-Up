using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;

namespace FollowUp.Infrastructure.Security;

/// <summary>
/// The principal used when no human caller is present — background jobs and startup seeding. Audited as
/// "system"; full scope so jobs can operate network-wide. The API overrides <see cref="ICurrentUser"/> with
/// an HttpContext-backed implementation for real requests.
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public AppUserId UserId { get; } = new(Guid.Empty);
    public string Username => "system";
    public RoleId RoleId { get; } = new(Guid.Empty);
    public UserSessionId? SessionId => null;
    public IReadOnlySet<string> Privileges => Domain.Identity.Privileges.All;
    public OrgScope Scope => OrgScope.Global;
    public RepresentativeId? RepresentativeId => null;
    public string? Ip => null;
    public string? CorrelationId => "system-job";
    public bool Has(string privilege) => true;
}
