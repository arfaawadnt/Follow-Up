using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Representatives.UpdateRepresentative;

/// <summary>Updates a representative's profile (SRS FR-4). Optimistic-concurrency guarded (409 on stale).</summary>
public sealed record UpdateRepresentativeCommand : ICommand, IAuthorizedRequest
{
    public Guid Id { get; init; }
    public uint RowVersion { get; init; }
    public string FullName { get; init; } = string.Empty;
    public decimal Salary { get; init; }
    public decimal Target { get; init; }
    public string? GoalType { get; init; }
    public string? Metric { get; init; }
    public string? Phone { get; init; }
    public string? Branch { get; init; }
    public string? Governorate { get; init; }
    public string? Area { get; init; }
    public string? EmploymentType { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateReps, Privileges.ManageReps };
}

public sealed class UpdateRepresentativeValidator : AbstractValidator<UpdateRepresentativeCommand>
{
    public UpdateRepresentativeValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Salary).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Target).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateRepresentativeHandler : ICommandHandler<UpdateRepresentativeCommand>
{
    private readonly IRepresentativeRepository _repository;

    public UpdateRepresentativeHandler(IRepresentativeRepository repository) => _repository = repository;

    public async Task<Unit> Handle(UpdateRepresentativeCommand request, CancellationToken ct)
    {
        var rep = await _repository.GetByIdAsync(new RepresentativeId(request.Id), ct)
            ?? throw new NotFoundException("Representative", request.Id);

        if (rep.RowVersion != request.RowVersion)
            throw new ConflictException("The representative was modified by someone else. Reload and try again.");

        rep.UpdateProfile(request.FullName, new Money(request.Salary), new Money(request.Target), request.GoalType, request.Metric);
        rep.SetContact(request.Phone);
        rep.AssignScope(request.Branch, request.Governorate, request.Area);
        rep.SetEmployment(request.EmploymentType);
        return Unit.Value;
    }
}
