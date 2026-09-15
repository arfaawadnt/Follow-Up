using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Features.Laboratories.UpdateLaboratory;
using FollowUp.Application.Features.Setup;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Tests.Features.Setup;

/// <summary>
/// Management roles: the area-manager and lab-responsible pickers are bound to the matching rep type, and the handlers
/// enforce the same rule server-side (a rep of any other type is refused; an unknown rep is not found). The responsible
/// lives on the laboratory (type LabResponsible) since 2026-09-16.
/// </summary>
public class AreaRolesHandlerTests
{
    private static Representative Rep(RepresentativeType type) =>
        Representative.Register($"{type.Name} Rep", type, GoalDuration.Monthly, Money.Zero, Money.Zero);

    private static CreateLaboratoryCommand LabCommand(Guid? responsible) => new()
    {
        Code = $"MGL-{Random.Shared.Next(1000, 9999)}",
        Name = "Nile Diagnostics",
        Segment = "B",
        Governorate = "Cairo",
        WorkDays = new[] { "Monday" },
        VisitTimes = new[] { "09:00" },
        ResponsibleRepId = responsible,
    };
    private static FakeCurrentUser LabUser() => new() { Privileges = new HashSet<string> { Privileges.AddLabs, Privileges.UpdateLabs } };

    [Fact]
    public async Task Create_area_assigns_a_manager_of_the_matching_type()
    {
        var mgr = Rep(RepresentativeType.AreaManager);
        var reps = new FakeRepresentativeRepository(); reps.Store.Add(mgr);
        var areas = new FakeAreaRepository();
        var handler = new CreateAreaHandler(areas, reps, new FakeCurrentUser());

        var id = await handler.Handle(new CreateAreaCommand("Nasr City", Guid.NewGuid(), false, Array.Empty<Guid>(),
            AreaManagerId: mgr.Id.Value), CancellationToken.None);

        areas.Store.Single(a => a.Id.Value == id).AreaManagerId.Should().Be(mgr.Id);
    }

    [Fact]
    public async Task Create_area_rejects_a_manager_whose_type_is_not_AreaManager()
    {
        var responsible = Rep(RepresentativeType.LabResponsible);
        var reps = new FakeRepresentativeRepository(); reps.Store.Add(responsible);
        var areas = new FakeAreaRepository();
        var handler = new CreateAreaHandler(areas, reps, new FakeCurrentUser());

        var act = () => handler.Handle(new CreateAreaCommand("Nasr City", Guid.NewGuid(), false, Array.Empty<Guid>(),
            AreaManagerId: responsible.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        areas.Store.Should().BeEmpty("nothing is created when the assignment is invalid");
    }

    [Fact]
    public async Task Update_area_reassigns_and_clears_the_manager_and_refuses_an_unknown_one()
    {
        var mgr1 = Rep(RepresentativeType.AreaManager); var mgr2 = Rep(RepresentativeType.AreaManager);
        var reps = new FakeRepresentativeRepository(); reps.Store.Add(mgr1); reps.Store.Add(mgr2);
        var cityId = CityId.New();
        var area = Area.Create("Nasr City", cityId, false); area.AssignManager(mgr1.Id);
        var areas = new FakeAreaRepository(); areas.Store.Add(area);
        var handler = new UpdateAreaHandler(areas, reps, new FakeCurrentUser());

        await handler.Handle(new UpdateAreaCommand(area.Id.Value, "Nasr City", cityId.Value, false, AreaManagerId: mgr2.Id.Value), CancellationToken.None);
        area.AreaManagerId.Should().Be(mgr2.Id, "the manager was reassigned");

        await handler.Handle(new UpdateAreaCommand(area.Id.Value, "Nasr City", cityId.Value, false, AreaManagerId: null), CancellationToken.None);
        area.AreaManagerId.Should().BeNull("the manager was cleared");

        await FluentActions.Awaiting(() => handler.Handle(new UpdateAreaCommand(area.Id.Value, "Nasr City", cityId.Value, false, AreaManagerId: Guid.NewGuid()), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Create_lab_assigns_a_responsible_of_type_LabResponsible_and_refuses_any_other_type_or_an_unknown_rep()
    {
        var responsible = Rep(RepresentativeType.LabResponsible); var mgr = Rep(RepresentativeType.AreaManager);
        var reps = new FakeRepresentativeRepository(); reps.Store.Add(responsible); reps.Store.Add(mgr);
        var labs = new FakeLaboratoryRepository();
        var handler = new CreateLaboratoryHandler(labs, LabUser(), new FakeSetupQueries(), reps);

        var id = await handler.Handle(LabCommand(responsible.Id.Value), CancellationToken.None);
        labs.Store.Single(l => l.Id.Value == id).ResponsibleRepId.Should().Be(responsible.Id);

        await FluentActions.Awaiting(() => handler.Handle(LabCommand(mgr.Id.Value), CancellationToken.None))
            .Should().ThrowAsync<ValidationException>("an AreaManager offered as the lab responsible violates the type binding");
        await FluentActions.Awaiting(() => handler.Handle(LabCommand(Guid.NewGuid()), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();
        labs.Store.Should().ContainSingle("nothing is created when the assignment is invalid");
    }

    [Fact]
    public async Task Update_lab_reassigns_and_clears_the_responsible()
    {
        var r1 = Rep(RepresentativeType.LabResponsible); var r2 = Rep(RepresentativeType.LabResponsible);
        var reps = new FakeRepresentativeRepository(); reps.Store.Add(r1); reps.Store.Add(r2);
        var labs = new FakeLaboratoryRepository(); var user = LabUser();
        var id = await new CreateLaboratoryHandler(labs, user, new FakeSetupQueries(), reps).Handle(LabCommand(r1.Id.Value), CancellationToken.None);
        var lab = labs.Store.Single();
        var handler = new UpdateLaboratoryHandler(labs, user, new FakeSetupQueries(), reps);
        UpdateLaboratoryCommand Cmd(Guid? responsible) => new()
        {
            Id = id,
            Name = lab.Name,
            Segment = lab.Segment,
            Governorate = "Cairo",
            WorkDays = new[] { "Monday" },
            VisitTimes = new[] { "09:00" },
            RowVersion = lab.RowVersion,
            ResponsibleRepId = responsible,
        };

        await handler.Handle(Cmd(r2.Id.Value), CancellationToken.None);
        lab.ResponsibleRepId.Should().Be(r2.Id, "the responsible was reassigned");

        await handler.Handle(Cmd(null), CancellationToken.None);
        lab.ResponsibleRepId.Should().BeNull("the responsible was cleared");
    }
}
