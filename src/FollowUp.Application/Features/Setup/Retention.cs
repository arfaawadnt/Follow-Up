using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Reference;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Setup;

/// <summary>Executes the bounded retention purge (SRS FR-18). Implemented by the retention service in Infrastructure.</summary>
public interface IRetentionRunner
{
    Task<int> PurgeAsync(CancellationToken ct);
}

public sealed record RetentionDto(int? Days, bool Enabled);

public sealed record GetRetentionQuery : IQuery<RetentionDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers, Privileges.SetupRefs };
}

public sealed class GetRetentionHandler : IQueryHandler<GetRetentionQuery, RetentionDto>
{
    private readonly IAppSettingRepository _settings;
    public GetRetentionHandler(IAppSettingRepository settings) => _settings = settings;

    public async Task<RetentionDto> Handle(GetRetentionQuery request, CancellationToken ct)
    {
        var setting = await _settings.GetAsync("retention.days", ct);
        return int.TryParse(setting?.Value, out var days) ? new RetentionDto(days, true) : new RetentionDto(null, false);
    }
}

/// <summary>Sets the retention window (minimum 30 days; SRS FR-18).</summary>
public sealed record SetRetentionCommand(int Days) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers, Privileges.SetupRefs };
}

public sealed class SetRetentionValidator : FluentValidation.AbstractValidator<SetRetentionCommand>
{
    public SetRetentionValidator() =>
        RuleFor(x => x.Days).GreaterThanOrEqualTo(30).WithMessage("Retention must be at least 30 days.");
}

public sealed class SetRetentionHandler : ICommandHandler<SetRetentionCommand>
{
    private readonly IAppSettingRepository _settings;
    public SetRetentionHandler(IAppSettingRepository settings) => _settings = settings;

    public async Task<Unit> Handle(SetRetentionCommand request, CancellationToken ct)
    {
        var setting = await _settings.GetAsync("retention.days", ct);
        if (setting is null) _settings.Add(AppSetting.Create("retention.days", request.Days.ToString(), isSecret: false));
        else setting.SetValue(request.Days.ToString());
        return Unit.Value;
    }
}

/// <summary>Runs the retention purge on demand (SRS FR-18).</summary>
public sealed record RunRetentionCommand : ICommand<int>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers, Privileges.SetupRefs };
}

public sealed class RunRetentionHandler : ICommandHandler<RunRetentionCommand, int>
{
    private readonly IRetentionRunner _runner;
    public RunRetentionHandler(IRetentionRunner runner) => _runner = runner;
    public Task<int> Handle(RunRetentionCommand request, CancellationToken ct) => _runner.PurgeAsync(ct);
}
