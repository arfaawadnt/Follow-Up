using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
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
