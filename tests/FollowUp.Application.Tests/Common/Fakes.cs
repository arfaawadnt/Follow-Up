using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
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
