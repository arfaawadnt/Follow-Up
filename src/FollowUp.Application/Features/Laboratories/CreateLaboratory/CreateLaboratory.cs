using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FluentValidation;

namespace FollowUp.Application.Features.Laboratories.CreateLaboratory;

/// <summary>Onboards a new client laboratory with its contacts, schedule and rep assignments (SRS FR-3, BR-3).</summary>
public sealed record CreateLaboratoryCommand : ICommand<Guid>, IAuthorizedRequest
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Segment { get; init; } = "C";
    public string? Branch { get; init; }
    public string? Governorate { get; init; }
    public string? City { get; init; }
    public string? Area { get; init; }
    public string? Category { get; init; }
    public string? Payer { get; init; }
    public string? ContractType { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public IReadOnlyList<string> WorkDays { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> VisitTimes { get; init; } = Array.Empty<string>();
    public Guid? CollectorRepId { get; init; }
    public Guid? MarketingRepId { get; init; }
    public IReadOnlyList<NewContact> Contacts { get; init; } = Array.Empty<NewContact>();

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddLabs, Privileges.ManageLabs };
}

public sealed record NewContact(string Name, string Role, string? Phone, DateOnly? Birthday);

public sealed class CreateLaboratoryValidator : AbstractValidator<CreateLaboratoryCommand>
{
    public CreateLaboratoryValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Segment).Must(s => s is "A" or "B" or "C").WithMessage("Segment must be A, B or C.");
        RuleForEach(x => x.Contacts).ChildRules(c =>
        {
            c.RuleFor(x => x.Name).NotEmpty();
            c.RuleFor(x => x.Role).Must(r => r is "Manager" or "Receptionist")
                .WithMessage("Contact role must be Manager or Receptionist.");
        });
        RuleFor(x => x).Must(HaveValidGeo).WithMessage("Latitude and longitude must be provided together.");
    }

    private static bool HaveValidGeo(CreateLaboratoryCommand c) =>
        c.Latitude.HasValue == c.Longitude.HasValue;
}

public sealed class CreateLaboratoryHandler : ICommandHandler<CreateLaboratoryCommand, Guid>
{
    private readonly ILaboratoryRepository _repository;
    private readonly ICurrentUser _currentUser;

    public CreateLaboratoryHandler(ILaboratoryRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(CreateLaboratoryCommand request, CancellationToken ct)
    {
        _currentUser.EnsureHierarchyInScope(request.Branch, request.Governorate, request.City,
            request.Area, request.Category, request.Segment);

        var code = LabCode.Create(request.Code);
        if (await _repository.CodeExistsAsync(code, ct))
            throw new ConflictException($"A laboratory with code '{code}' already exists.");

        var lab = Laboratory.Register(code, request.Name, Segment.FromName<Segment>(request.Segment));
        lab.PlaceInHierarchy(request.Branch, request.Governorate, request.City, request.Area);
        lab.UpdateProfile(request.Name, Segment.FromName<Segment>(request.Segment),
            request.Payer, request.ContractType, request.Category);

        if (request.Latitude is { } lat && request.Longitude is { } lng)
            lab.SetLocation(GeoLocation.Create(lat, lng));

        lab.SetSchedule(BuildSchedule(request.WorkDays, request.VisitTimes));

        if (request.CollectorRepId is { } c) lab.AssignCollector(new Domain.Representatives.RepresentativeId(c));
        if (request.MarketingRepId is { } m) lab.AssignMarketing(new Domain.Representatives.RepresentativeId(m));

        foreach (var contact in request.Contacts)
            lab.AddContact(contact.Name, Enum.Parse<ContactRole>(contact.Role), contact.Phone, contact.Birthday);

        _repository.Add(lab);
        return lab.Id.Value;
    }

    internal static VisitSchedule BuildSchedule(IReadOnlyList<string> workDays, IReadOnlyList<string> visitTimes)
    {
        var days = workDays.Select(d => Enum.Parse<DayOfWeek>(d, ignoreCase: true));
        var times = visitTimes.Select(t => TimeOnly.Parse(t));
        return VisitSchedule.Create(days, times);
    }
}
