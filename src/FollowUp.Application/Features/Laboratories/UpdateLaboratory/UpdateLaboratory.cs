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

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateLabs, Privileges.ManageLabs };
}

public sealed class UpdateLaboratoryValidator : AbstractValidator<UpdateLaboratoryCommand>
{
    public UpdateLaboratoryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Segment).NotEmpty().WithMessage("Segment is required.");
        RuleFor(x => x).Must(c => c.Latitude.HasValue == c.Longitude.HasValue)
            .WithMessage("Latitude and longitude must be provided together.");
    }
}

public sealed class UpdateLaboratoryHandler : ICommandHandler<UpdateLaboratoryCommand>
{
    private readonly ILaboratoryRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly Setup.ISetupQueries _setup;

    public UpdateLaboratoryHandler(ILaboratoryRepository repository, ICurrentUser currentUser, Setup.ISetupQueries setup)
    {
        _repository = repository;
        _currentUser = currentUser;
        _setup = setup;
    }

    public async Task<Unit> Handle(UpdateLaboratoryCommand request, CancellationToken ct)
    {
        var lab = await _repository.GetByIdAsync(new LaboratoryId(request.Id), ct)
            ?? throw new NotFoundException("Laboratory", request.Id);

        _currentUser.EnsureInScope(lab);
        // Then verify the caller is allowed to move it to the new hierarchy/segment.
        _currentUser.EnsureHierarchyInScope(request.Branch, request.Governorate, request.City,
            request.Area, request.Category, request.Segment);
        await CreateLaboratory.CreateLaboratoryHandler.EnsureSegmentConfiguredAsync(_setup, request.Segment, ct);

        // Optimistic concurrency (FR-3): reject an edit made against a stale version.
        if (lab.RowVersion != request.RowVersion)
            throw new ConflictException("The laboratory was modified by someone else. Reload and try again.");

        lab.UpdateProfile(request.Name, request.Segment, request.Payer, request.ContractType, request.Category,
            request.LicenseNo, request.LicenseDate, request.AvgMonthlySamples, request.PreferredChannel);
        lab.PlaceInHierarchy(request.Branch, request.Governorate, request.City, request.Area);
        lab.SetLocation(request.Latitude is { } lat && request.Longitude is { } lng ? GeoLocation.Create(lat, lng) : null);
        lab.SetSchedule(CreateLaboratoryHandler.BuildSchedule(request.WorkDays, request.VisitTimes));
        lab.AssignCollectors(request.CollectorRepIds.Select(c => new RepresentativeId(c)));
        lab.AssignMarketing(request.MarketingRepId is { } m ? new RepresentativeId(m) : null);

        // Replace contacts (FR-3: contacts saved with the lab in one transaction).
        foreach (var existing in lab.Contacts.ToList())
            lab.RemoveContact(existing.Id);
        foreach (var contact in request.Contacts)
            lab.AddContact(contact.Name, Enum.Parse<ContactRole>(contact.Role), contact.Phone, contact.Birthday);

        return Unit.Value;
    }
}
