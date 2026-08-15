using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Laboratories.ChangeLaboratoryStatus;

/// <summary>Changes a laboratory's status through its validated set (SRS FR-3, BR-5).</summary>
public sealed record ChangeLaboratoryStatusCommand(Guid LaboratoryId, string Status)
    : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateLabs, Privileges.ManageLabs };
}

public sealed class ChangeLaboratoryStatusValidator : AbstractValidator<ChangeLaboratoryStatusCommand>
{
    public ChangeLaboratoryStatusValidator()
    {
        RuleFor(x => x.LaboratoryId).NotEmpty();
        RuleFor(x => x.Status).NotEmpty();
    }
}

public sealed class ChangeLaboratoryStatusHandler : ICommandHandler<ChangeLaboratoryStatusCommand>
{
    private readonly ILaboratoryRepository _repository;
    private readonly ICurrentUser _currentUser;

    public ChangeLaboratoryStatusHandler(ILaboratoryRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(ChangeLaboratoryStatusCommand request, CancellationToken ct)
    {
        var lab = await _repository.GetByIdAsync(new LaboratoryId(request.LaboratoryId), ct)
            ?? throw new NotFoundException("Laboratory", request.LaboratoryId);

        _currentUser.EnsureInScope(lab);

        lab.ChangeStatus(LaboratoryStatus.FromName<LaboratoryStatus>(request.Status));
        return Unit.Value;
    }
}
