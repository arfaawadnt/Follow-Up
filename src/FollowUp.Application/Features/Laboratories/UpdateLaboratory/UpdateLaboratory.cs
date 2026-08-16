using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Laboratories.UpdateLaboratory;

/// <summary>Updates a laboratory's profile, hierarchy, schedule, location, rep assignments and contacts
/// (SRS FR-3). Optimistic-concurrency guarded: a stale <see cref="RowVersion"/> yields 409.</summary>
public sealed record UpdateLaboratoryCommand : ICommand, IAuthorizedRequest
{
    public Guid Id { get; init; }
    public uint RowVersion { get; init; }
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

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateLabs, Privileges.ManageLabs };
}

public sealed class UpdateLaboratoryValidator : AbstractValidator<UpdateLaboratoryCommand>
{
    public UpdateLaboratoryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Segment).Must(s => s is "A" or "B" or "C").WithMessage("Segment must be A, B or C.");
        RuleFor(x => x).Must(c => c.Latitude.HasValue == c.Longitude.HasValue)
            .WithMessage("Latitude and longitude must be provided together.");
    }
}

public sealed class UpdateLaboratoryHandler : ICommandHandler<UpdateLaboratoryCommand>
{
    private readonly ILaboratoryRepository _repository;
    private readonly ICurrentUser _currentUser;

    public UpdateLaboratoryHandler(ILaboratoryRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(UpdateLaboratoryCommand request, CancellationToken ct)
    {
        var lab = await _repository.GetByIdAsync(new LaboratoryId(request.Id), ct)
            ?? throw new NotFoundException("Laboratory", request.Id);

        _currentUser.EnsureInScope(lab);
        // Then verify the caller is allowed to move it to the new hierarchy/segment.
        _currentUser.EnsureHierarchyInScope(request.Branch, request.Governorate, request.City,
            request.Area, request.Category, request.Segment);

        // Optimistic concurrency (FR-3): reject an edit made against a stale version.
        if (lab.RowVersion != request.RowVersion)
            throw new ConflictException("The laboratory was modified by someone else. Reload and try again.");

        var segment = Enumeration.FromName<Segment>(request.Segment);
        lab.UpdateProfile(request.Name, segment, request.Payer, request.ContractType, request.Category);
        lab.PlaceInHierarchy(request.Branch, request.Governorate, request.City, request.Area);
        lab.SetLocation(request.Latitude is { } lat && request.Longitude is { } lng ? GeoLocation.Create(lat, lng) : null);
        lab.SetSchedule(CreateLaboratoryHandler.BuildSchedule(request.WorkDays, request.VisitTimes));
        lab.AssignCollector(request.CollectorRepId is { } c ? new RepresentativeId(c) : null);
        lab.AssignMarketing(request.MarketingRepId is { } m ? new RepresentativeId(m) : null);

        // Replace contacts (FR-3: contacts saved with the lab in one transaction).
        foreach (var existing in lab.Contacts.ToList())
            lab.RemoveContact(existing.Id);
        foreach (var contact in request.Contacts)
            lab.AddContact(contact.Name, Enum.Parse<ContactRole>(contact.Role), contact.Phone, contact.Birthday);

        return Unit.Value;
    }
}
