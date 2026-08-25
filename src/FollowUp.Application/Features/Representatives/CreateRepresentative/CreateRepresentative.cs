using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;
using FluentValidation;

namespace FollowUp.Application.Features.Representatives.CreateRepresentative;

/// <summary>Registers a new representative (SRS FR-4).</summary>
public sealed record CreateRepresentativeCommand : ICommand<Guid>, IAuthorizedRequest
{
    public string FullName { get; init; } = string.Empty;
    public string Type { get; init; } = "Collector";
    public string GoalDuration { get; init; } = "Monthly";
    public decimal Salary { get; init; }
    public decimal Target { get; init; }
    public string? GoalType { get; init; }
    public string? Metric { get; init; }
    public string? Phone { get; init; }
    public string? Branch { get; init; }
    public string? Governorate { get; init; }
    public string? Area { get; init; }
    public string? EmploymentType { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddReps, Privileges.ManageReps };
}

public sealed class CreateRepresentativeValidator : AbstractValidator<CreateRepresentativeCommand>
{
    private static readonly string[] Types = { "Collector", "Marketing", "Transfer", "Scanning" };

    public CreateRepresentativeValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).Must(t => Types.Contains(t)).WithMessage("Invalid representative type.");
        RuleFor(x => x.GoalDuration).Must(d => d is "Monthly" or "Quarterly").WithMessage("Goal duration must be Monthly or Quarterly.");
        RuleFor(x => x.Salary).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Target).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateRepresentativeHandler : ICommandHandler<CreateRepresentativeCommand, Guid>
{
    private readonly IRepresentativeRepository _repository;

    public CreateRepresentativeHandler(IRepresentativeRepository repository) => _repository = repository;

    public Task<Guid> Handle(CreateRepresentativeCommand request, CancellationToken ct)
    {
        var rep = Representative.Register(
            request.FullName,
            Enumeration.FromName<RepresentativeType>(request.Type),
            Enumeration.FromName<GoalDuration>(request.GoalDuration),
            new Money(request.Salary),
            new Money(request.Target));

        rep.SetContact(request.Phone);
        rep.AssignScope(request.Branch, request.Governorate, request.Area);
        rep.SetEmployment(request.EmploymentType);
        if (request.GoalType is not null || request.Metric is not null)
            rep.UpdateProfile(request.FullName, new Money(request.Salary), new Money(request.Target), request.GoalType, request.Metric);

        _repository.Add(rep);
        return Task.FromResult(rep.Id.Value);
    }
}
