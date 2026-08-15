using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Integration;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Integration;

// ---- Read config (never exposes the connection string) ----

public sealed record OracleConfigDto(
    bool Enabled, int IntervalHours, IReadOnlyList<string> AllowListedQueries,
    DateTimeOffset? LastSyncAt, string? LastStatus);

/// <summary>Reads the Oracle integration config (SRS FR-17). The connection string is never returned.</summary>
public sealed record GetIntegrationConfigQuery : IQuery<OracleConfigDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OracleIntegration };
}

public sealed class GetIntegrationConfigHandler : IQueryHandler<GetIntegrationConfigQuery, OracleConfigDto>
{
    private readonly IOracleConfigRepository _repo;
    public GetIntegrationConfigHandler(IOracleConfigRepository repo) => _repo = repo;

    public async Task<OracleConfigDto> Handle(GetIntegrationConfigQuery r, CancellationToken ct)
    {
        var cfg = await _repo.GetAsync(ct)
            ?? throw new NotFoundException("Oracle configuration has not been provisioned.");
        return new OracleConfigDto(cfg.Enabled, cfg.IntervalHours,
            cfg.Queries.Select(q => q.Name).ToArray(), cfg.LastSyncAt, cfg.LastStatus);
    }
}

// ---- Update config (enable + interval ONLY) ----

/// <summary>
/// Updates only the enable flag and interval (SRS FR-17). The SQL text and connection string are
/// config-managed and are not writable via the API — any attempt to change them is out of contract.
/// </summary>
public sealed record UpdateIntegrationConfigCommand(bool Enabled, int IntervalHours) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OracleIntegration };
}

public sealed class UpdateIntegrationConfigValidator : AbstractValidator<UpdateIntegrationConfigCommand>
{
    public UpdateIntegrationConfigValidator() => RuleFor(x => x.IntervalHours).GreaterThanOrEqualTo(1);
}

public sealed class UpdateIntegrationConfigHandler : ICommandHandler<UpdateIntegrationConfigCommand>
{
    private readonly IOracleConfigRepository _repo;
    public UpdateIntegrationConfigHandler(IOracleConfigRepository repo) => _repo = repo;

    public async Task<Unit> Handle(UpdateIntegrationConfigCommand r, CancellationToken ct)
    {
        var cfg = await _repo.GetAsync(ct);
        if (cfg is null)
        {
            cfg = OracleConfig.Create(r.Enabled, r.IntervalHours);
            _repo.Add(cfg);
        }
        else
        {
            cfg.Configure(r.Enabled, r.IntervalHours);
        }
        return Unit.Value;
    }
}

// ---- Sync now (manual trigger) ----

public sealed record SyncOracleNowCommand : ICommand<OracleSyncResult>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OracleIntegration };
}

public sealed class SyncOracleNowHandler : ICommandHandler<SyncOracleNowCommand, OracleSyncResult>
{
    private readonly IOracleSyncRunner _runner;
    public SyncOracleNowHandler(IOracleSyncRunner runner) => _runner = runner;

    public Task<OracleSyncResult> Handle(SyncOracleNowCommand r, CancellationToken ct) =>
        _runner.RunAsync(manual: true, ct);
}
