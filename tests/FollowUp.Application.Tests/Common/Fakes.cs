using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Operations;
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
