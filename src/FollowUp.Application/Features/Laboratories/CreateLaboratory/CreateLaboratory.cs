using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Application.Features.Setup;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
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
    public string? Status { get; init; }
    public string? LicenseNo { get; init; }
    public DateOnly? LicenseDate { get; init; }
    public int? AvgMonthlySamples { get; init; }
    public string? PreferredChannel { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public IReadOnlyList<string> WorkDays { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> VisitTimes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Guid> CollectorRepIds { get; init; } = Array.Empty<Guid>();
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
        RuleFor(x => x.Segment).NotEmpty().WithMessage("Segment is required.");
        RuleFor(x => x.Status).Must(BeAValidStatus).WithMessage("Invalid laboratory status.");
        RuleFor(x => x.AvgMonthlySamples).GreaterThanOrEqualTo(0).When(x => x.AvgMonthlySamples.HasValue);
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

    internal static readonly string[] AllowedStatuses =
        { "Interactive", "Scanned", "Active", "Inactive", "Pending", "Suspended", "Stopped", "Churned" };
    private static bool BeAValidStatus(string? s) => string.IsNullOrEmpty(s) || AllowedStatuses.Contains(s);
}

public sealed class CreateLaboratoryHandler : ICommandHandler<CreateLaboratoryCommand, Guid>
{
    private readonly ILaboratoryRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ISetupQueries _setup;

    public CreateLaboratoryHandler(ILaboratoryRepository repository, ICurrentUser currentUser, ISetupQueries setup)
    {
        _repository = repository;
        _currentUser = currentUser;
        _setup = setup;
    }

    public async Task<Guid> Handle(CreateLaboratoryCommand request, CancellationToken ct)
    {
        _currentUser.EnsureHierarchyInScope(request.Branch, request.Governorate, request.City,
            request.Area, request.Category, request.Segment);
        await EnsureSegmentConfiguredAsync(_setup, request.Segment, ct);

        var code = LabCode.Create(request.Code);
        if (await _repository.CodeExistsAsync(code, ct))
            throw new ConflictException($"A laboratory with code '{code}' already exists.");

        var status = string.IsNullOrWhiteSpace(request.Status) ? null : Enumeration.FromName<LaboratoryStatus>(request.Status);
        var lab = Laboratory.Register(code, request.Name, request.Segment, status);
        lab.PlaceInHierarchy(request.Branch, request.Governorate, request.City, request.Area);
        lab.UpdateProfile(request.Name, request.Segment, request.Payer, request.ContractType, request.Category,
            request.LicenseNo, request.LicenseDate, request.AvgMonthlySamples, request.PreferredChannel);

        if (request.Latitude is { } lat && request.Longitude is { } lng)
            lab.SetLocation(GeoLocation.Create(lat, lng));

        lab.SetSchedule(BuildSchedule(request.WorkDays, request.VisitTimes));

        lab.AssignCollectors(request.CollectorRepIds.Select(c => new Domain.Representatives.RepresentativeId(c)));
        if (request.MarketingRepId is { } m) lab.AssignMarketing(new Domain.Representatives.RepresentativeId(m));

        foreach (var contact in request.Contacts)
            lab.AddContact(contact.Name, Enum.Parse<ContactRole>(contact.Role), contact.Phone, contact.Birthday);

        _repository.Add(lab);
        return lab.Id.Value;
    }

    /// <summary>Segments are configurable reference data (RefType.Segment): reject any value not configured (400).</summary>
    internal static async Task EnsureSegmentConfiguredAsync(ISetupQueries setup, string segment, CancellationToken ct)
    {
        var configured = await setup.GetRefItemsAsync(nameof(RefType.Segment), ct);
        if (!configured.Any(s => string.Equals(s.Code, segment?.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new DomainException($"'{segment}' is not a configured segment. Add it under Setup → Segments.");
    }

    internal static VisitSchedule BuildSchedule(IReadOnlyList<string> workDays, IReadOnlyList<string> visitTimes)
    {
        // Parse defensively: invalid user text must surface as a 400 (DomainException), not an opaque 500.
        var days = new List<DayOfWeek>(workDays.Count);
        foreach (var d in workDays)
        {
            if (!Enum.TryParse<DayOfWeek>(d?.Trim(), ignoreCase: true, out var day))
                throw new DomainException($"'{d}' is not a valid work day. Use full weekday names, e.g. Sunday.");
            days.Add(day);
        }

        var times = new List<TimeOnly>(visitTimes.Count);
        foreach (var t in visitTimes)
        {
            if (!TimeOnly.TryParse(t?.Trim(), out var time))
                throw new DomainException($"'{t}' is not a valid visit time. Use 24-hour HH:mm, e.g. 09:00.");
            times.Add(time);
        }

        return VisitSchedule.Create(days, times);
    }
}
