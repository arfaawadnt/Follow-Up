using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Tests.Common;

/// <summary>In-memory laboratory repository for handler tests.</summary>
public sealed class FakeLaboratoryRepository : ILaboratoryRepository
{
    public readonly List<Laboratory> Store = new();
    private int _seq;

    public Task<Laboratory?> GetByIdAsync(LaboratoryId id, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(l => l.Id == id));

    public Task<Laboratory?> GetByCodeAsync(LabCode code, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(l => l.Code == code));

    public Task<bool> CodeExistsAsync(LabCode code, CancellationToken ct) =>
        Task.FromResult(Store.Any(l => l.Code == code));

    public Task<string> NextCodeAsync(CancellationToken ct) =>
        Task.FromResult($"MGL-{++_seq:0000}");

    public void Add(Laboratory laboratory) => Store.Add(laboratory);
}

/// <summary>Configurable current-user stub.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; init; } = true;
    public AppUserId UserId { get; init; } = AppUserId.New();
    public string Username { get; init; } = "tester";
    public RoleId RoleId { get; init; } = RoleId.New();
    public UserSessionId? SessionId { get; init; }
    public IReadOnlySet<string> Privileges { get; init; } = new HashSet<string>();
    public OrgScope Scope { get; init; } = OrgScope.Global;
    public RepresentativeId? RepresentativeId { get; init; }
    public string? Ip { get; init; }
    public string? CorrelationId { get; init; } = "test-corr";

    public bool Has(string privilege) => Privileges.Contains(privilege);
}

/// <summary>Fixed clock for deterministic handler tests.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) { UtcNow = now; CairoNow = now; CairoToday = DateOnly.FromDateTime(now.DateTime); }
    public DateTimeOffset UtcNow { get; }
    public DateTimeOffset CairoNow { get; }
    public DateOnly CairoToday { get; }
}

/// <summary>In-memory complaint repository for handler tests.</summary>
public sealed class FakeComplaintRepository : IComplaintRepository
{
    public readonly List<Complaint> Store = new();
    private int _seq;

    public Task<Complaint?> GetByIdAsync(ComplaintId id, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(c => c.Id == id));

    public Task<int> NextNumberAsync(CancellationToken ct) => Task.FromResult(++_seq);

    public void Add(Complaint complaint) => Store.Add(complaint);
}

/// <summary>Configurable e-signature gate: enforcement on/off and whether a valid signature exists.</summary>
public sealed class FakeSignatureGate : IElectronicSignatureGate
{
    public bool Enforced { get; init; }
    public bool HasValid { get; init; }
    public Task<bool> IsEnforcedAsync(string module, CancellationToken ct) => Task.FromResult(Enforced);
    public Task<bool> HasValidSignatureAsync(string module, string recordId, CancellationToken ct) => Task.FromResult(HasValid);
}

public sealed class FakeRepresentativeRepository : IRepresentativeRepository
{
    public readonly List<Representative> Store = new();
    public Task<Representative?> GetByIdAsync(RepresentativeId id, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(r => r.Id == id));
    public Task<bool> ExistsAsync(RepresentativeId id, CancellationToken ct) =>
        Task.FromResult(Store.Any(r => r.Id == id));
    public void Add(Representative representative) => Store.Add(representative);
}

public sealed class FakeDailyVisitRepository : IDailyVisitRepository
{
    public readonly List<DailyVisit> Store = new();
    public Task<DailyVisit?> GetByIdAsync(DailyVisitId id, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(v => v.Id == id));
    public void Add(DailyVisit visit) => Store.Add(visit);
}

public sealed class FakeMarketingVisitRepository : IMarketingVisitRepository
{
    public readonly List<MarketingVisit> Store = new();
    public Task<MarketingVisit?> GetByIdAsync(MarketingVisitId id, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(v => v.Id == id));
    public Task<int> NextNumberAsync(CancellationToken ct) =>
        Task.FromResult(Store.Count == 0 ? 1 : Store.Max(v => v.Number) + 1);
    public void Add(MarketingVisit visit) => Store.Add(visit);
}

public sealed class FakeOutsourceSampleRepository : IOutsourceSampleRepository
{
    public readonly List<OutsourceSample> Store = new();
    public Task<OutsourceSample?> GetByIdAsync(OutsourceSampleId id, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(s => s.Id == id));
    public Task<bool> ExistsForAsync(LaboratoryId labId, DateOnly visitDate, CancellationToken ct) =>
        Task.FromResult(Store.Any(s => s.LaboratoryId == labId && s.VisitDate == visitDate));
    public void Add(OutsourceSample sample) => Store.Add(sample);
    public void Remove(OutsourceSample sample) => Store.Remove(sample);
}

public sealed class FakeSampleTrackingRepository : ISampleTrackingRepository
{
    public readonly List<Domain.Operations.SampleTracking> Store = new();
    public Task<Domain.Operations.SampleTracking?> GetByIdAsync(SampleTrackingId id, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(s => s.Id == id));
    public Task<Domain.Operations.SampleTracking?> GetByAreaDateAsync(string area, DateOnly date, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(s => s.Area == area && s.Date == date));
    public void Add(Domain.Operations.SampleTracking tracking) => Store.Add(tracking);
    public void Remove(Domain.Operations.SampleTracking tracking) => Store.Remove(tracking);
}

public sealed class FakeRoleRepository : IRoleRepository
{
    public readonly List<Role> Store = new();
    public HashSet<RoleId> InUse { get; } = new();
    public Task<Role?> GetByIdAsync(RoleId id, CancellationToken ct) => Task.FromResult(Store.FirstOrDefault(r => r.Id == id));
    public Task<Role?> GetByNameAsync(string name, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)));
    public Task<bool> IsInUseAsync(RoleId id, CancellationToken ct) => Task.FromResult(InUse.Contains(id));
    public void Add(Role role) => Store.Add(role);
    public void Remove(Role role) => Store.Remove(role);
}

public sealed class FakeAppUserRepository : IAppUserRepository
{
    public readonly List<AppUser> Store = new();
    public Task<AppUser?> GetByIdAsync(AppUserId id, CancellationToken ct) => Task.FromResult(Store.FirstOrDefault(u => u.Id == id));
    public Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)));
    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct) =>
        Task.FromResult(Store.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)));
    public Task<bool> AnyLinkedToRepAsync(RepresentativeId repId, CancellationToken ct) =>
        Task.FromResult(Store.Any(u => u.RepresentativeId == repId));
    public void Add(AppUser user) => Store.Add(user);
    public void Remove(AppUser user) => Store.Remove(user);
}

/// <summary>Deterministic password hasher stub (not real crypto; Infra provides PBKDF2).</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public PasswordHash Hash(string password) => new("FAKE", 1, "c2FsdA==", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password)));
    public bool Verify(string password, PasswordHash hash) =>
        hash.Hash == Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
}

public sealed class FakeSystemNotificationRepository : ISystemNotificationRepository
{
    public readonly List<Domain.Notifications.SystemNotification> Store = new();
    public Task<Domain.Notifications.SystemNotification?> GetByIdAsync(Domain.Notifications.SystemNotificationId id, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(n => n.Id == id));
    public Task<IReadOnlyList<Domain.Notifications.SystemNotification>> GetUnreadForUserAsync(AppUserId userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Domain.Notifications.SystemNotification>>(Store.Where(n => n.RecipientUserId == userId && !n.IsRead).ToList());
    public void Add(Domain.Notifications.SystemNotification notification) => Store.Add(notification);
}

public sealed class FakeOracleConfigRepository : IOracleConfigRepository
{
    public Domain.Integration.OracleConfig? Config;
    public Task<Domain.Integration.OracleConfig?> GetAsync(CancellationToken ct) => Task.FromResult(Config);
    public void Add(Domain.Integration.OracleConfig config) => Config = config;
}

public sealed class FakeRepCommissionRepository : IRepCommissionRepository
{
    public readonly List<Domain.Compensation.RepCommission> Store = new();
    public Task<Domain.Compensation.RepCommission?> GetAsync(RepresentativeId repId, Domain.Common.YearMonth period, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(c => c.RepresentativeId == repId && c.Period == period));
    public void Add(Domain.Compensation.RepCommission commission) => Store.Add(commission);
}

public sealed class FakeCompensationConfigRepository : ICompensationConfigRepository
{
    public Domain.Compensation.CompensationConfig? Config;
    public Task<Domain.Compensation.CompensationConfig?> GetAsync(CancellationToken ct) => Task.FromResult(Config);
    public void Add(Domain.Compensation.CompensationConfig config) => Config = config;
}

public sealed class FakeCompensationData : ICompensationData
{
    public int LabAchieved { get; init; }
    public int RepAchieved { get; init; }
    public Task<int> GetLabAchievedSamplesAsync(LaboratoryId labId, Domain.Common.YearMonth period, CancellationToken ct) =>
        Task.FromResult(LabAchieved);
    public Task<int> GetRepAchievedSamplesAsync(RepresentativeId repId, Domain.Common.YearMonth period, CancellationToken ct) =>
        Task.FromResult(RepAchieved);
}

public sealed class FakeUserSessionRepository : IUserSessionRepository
{
    public readonly List<UserSession> Store = new();
    public Task<UserSession?> GetByIdAsync(UserSessionId id, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(s => s.Id == id));
    public Task<UserSession?> GetActiveByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(s => s.TokenHash == tokenHash));
    public void Add(UserSession session) => Store.Add(session);
}

public sealed class FakeTokenService : ITokenService
{
    public IssuedToken Issue(AppUserId userId, UserSessionId sessionId, DateTimeOffset issuedAt) =>
        new($"tok-{sessionId.Value}", $"hash-{sessionId.Value}", issuedAt.AddHours(10));
    public UserSessionId? ReadSessionId(string token) => null;
    public string HashToken(string token) => $"h:{token}";
}

public sealed class FakeAuthPolicy : IAuthPolicy
{
    public int MaxFailedAttempts { get; init; } = 10;
    public TimeSpan LockoutWindow { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromHours(10);
}

public sealed class FakeElectronicSignatureRepository : IElectronicSignatureRepository
{
    public readonly List<Domain.Signatures.ElectronicSignature> Store = new();
    public void Add(Domain.Signatures.ElectronicSignature signature) => Store.Add(signature);
    public Task<Domain.Signatures.ElectronicSignature?> GetLatestAsync(string module, string recordId, CancellationToken ct) =>
        Task.FromResult(Store.Where(s => s.Module == module && s.RecordId == recordId)
            .OrderByDescending(s => s.SignedAt).FirstOrDefault());
}

/// <summary>Record hasher whose returned hash/version can be changed to simulate a record edit.</summary>
public sealed class FakeRecordHasher : IRecordHasher
{
    public string Hash { get; set; } = "HASH-1";
    public uint Version { get; set; } = 1;
    public bool Exists { get; set; } = true;
    public Task<(string ContentHash, uint Version)?> ComputeAsync(string module, string recordId, CancellationToken ct) =>
        Task.FromResult(Exists ? ((string, uint)?)(Hash, Version) : null);
}

public sealed class FakeRefItemRepository : IRefItemRepository
{
    public readonly List<Domain.Reference.RefItem> Store = new();
    public Task<Domain.Reference.RefItem?> GetByIdAsync(Domain.Reference.RefItemId id, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(r => r.Id == id));
    public Task<bool> ExistsAsync(Domain.Reference.RefType type, string code, CancellationToken ct) =>
        Task.FromResult(Store.Any(r => r.Type == type && string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)));
    public void Add(Domain.Reference.RefItem item) => Store.Add(item);
    public void Remove(Domain.Reference.RefItem item) => Store.Remove(item);
}

/// <summary>In-memory setup queries; exposes A/B/C as configured segments for lab handler tests.</summary>
public sealed class FakeSetupQueries : Application.Features.Setup.ISetupQueries
{
    public Task<IReadOnlyList<Application.Features.Setup.RefItemDto>> GetRefItemsAsync(string? type, CancellationToken ct)
    {
        IReadOnlyList<Application.Features.Setup.RefItemDto> items = type == nameof(RefType.Segment)
            ? new[] { "A", "B", "C" }
                .Select((s, i) => new Application.Features.Setup.RefItemDto(Guid.NewGuid(), nameof(RefType.Segment), s, s, null, i))
                .ToList()
            : new List<Application.Features.Setup.RefItemDto>();
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<Application.Features.Setup.CityDto>> GetCitiesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Application.Features.Setup.CityDto>>(new List<Application.Features.Setup.CityDto>());

    public Task<IReadOnlyList<Application.Features.Setup.AreaDto>> GetAreasAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Application.Features.Setup.AreaDto>>(new List<Application.Features.Setup.AreaDto>());
}

/// <summary>No-op failed-login recorder for handler tests (persistence is proven in the integration suite).</summary>
public sealed class FakeFailedLoginRecorder : IFailedLoginRecorder
{
    public int Calls { get; private set; }
    public Task RecordAsync(AppUserId userId, CancellationToken ct)
    {
        Calls++;
        return Task.CompletedTask;
    }
}
