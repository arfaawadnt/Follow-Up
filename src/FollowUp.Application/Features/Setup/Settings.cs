using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Reference;
using MediatR;

namespace FollowUp.Application.Features.Setup;

public sealed record SettingDto(string Key, string? Value, bool IsSecret);

/// <summary>Read-side query for application settings; secret values are masked (SRS FR-2/NFR-SEC-7).</summary>
public interface ISettingsQueries
{
    Task<IReadOnlyList<SettingDto>> ListAsync(CancellationToken ct);
}

/// <summary>Lists application settings (admin); secrets masked.</summary>
public sealed record GetSettingsQuery : IQuery<IReadOnlyList<SettingDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}

public sealed class GetSettingsHandler : IQueryHandler<GetSettingsQuery, IReadOnlyList<SettingDto>>
{
    private readonly ISettingsQueries _queries;
    public GetSettingsHandler(ISettingsQueries queries) => _queries = queries;
    public Task<IReadOnlyList<SettingDto>> Handle(GetSettingsQuery request, CancellationToken ct) => _queries.ListAsync(ct);
}

/// <summary>Upserts an application setting (SRS FR-2). Secret-bearing keys are flagged so reads redact them.</summary>
public sealed record UpsertSettingCommand(string Key, string? Value, bool IsSecret) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}

public sealed class UpsertSettingHandler : ICommandHandler<UpsertSettingCommand>
{
    private readonly IAppSettingRepository _settings;
    public UpsertSettingHandler(IAppSettingRepository settings) => _settings = settings;

    public async Task<Unit> Handle(UpsertSettingCommand request, CancellationToken ct)
    {
        var setting = await _settings.GetAsync(request.Key, ct);
        if (setting is null)
        {
            _settings.Add(AppSetting.Create(request.Key, request.Value, request.IsSecret));
        }
        else
        {
            setting.SetValue(request.Value);
        }
        return Unit.Value;
    }
}
