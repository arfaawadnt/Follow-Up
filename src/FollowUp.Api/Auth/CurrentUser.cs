using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;

namespace FollowUp.Api.Auth;

/// <summary>The authenticated principal resolved by the token-auth middleware, stashed on the request.</summary>
public sealed record CurrentUserState(
    AppUserId UserId,
    string Username,
    RoleId RoleId,
    UserSessionId SessionId,
    IReadOnlySet<string> Privileges,
    OrgScope Scope,
    RepresentativeId? RepresentativeId);

/// <summary>
/// HttpContext-backed <see cref="ICurrentUser"/>. Reads the state the token-auth middleware placed on the
/// request (privileges + scope re-read from the DB on every request — SRS NFR-SEC-2). Unauthenticated when
/// absent (public endpoints), which the application authorization behavior turns into 401/403.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    public const string ItemKey = "follow-up.current-user";
    public const string CorrelationItemKey = "follow-up.correlation-id";

    private readonly IHttpContextAccessor _accessor;
    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private CurrentUserState? State => _accessor.HttpContext?.Items[ItemKey] as CurrentUserState;

    // No HttpContext at all => not an HTTP request (background job / startup) => the system principal.
    // HttpContext present but no resolved state => an anonymous (public) request.
    private bool IsBackground => _accessor.HttpContext is null;

    public bool IsAuthenticated => IsBackground || State is not null;
    public AppUserId UserId => State?.UserId ?? new AppUserId(Guid.Empty);
    public string Username => State?.Username ?? (IsBackground ? "system" : "anonymous");
    public RoleId RoleId => State?.RoleId ?? new RoleId(Guid.Empty);
    public UserSessionId? SessionId => State?.SessionId;
    public IReadOnlySet<string> Privileges =>
        State?.Privileges ?? (IsBackground ? Domain.Identity.Privileges.All : new HashSet<string>());
    public OrgScope Scope => State?.Scope ?? (IsBackground ? OrgScope.Global : OrgScope.Deny);
    public RepresentativeId? RepresentativeId => State?.RepresentativeId;
    public string? Ip => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => _accessor.HttpContext?.Items[CorrelationItemKey] as string ?? (IsBackground ? "system-job" : null);

    public bool Has(string privilege) => IsBackground || Privileges.Contains(privilege);
}
