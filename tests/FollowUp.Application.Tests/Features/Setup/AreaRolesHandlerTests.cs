using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Setup;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Common;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Tests.Features.Setup;

/// <summary>
/// Area management roles: the manager/responsible pickers are bound to the matching rep type, and the handlers
/// enforce the same rule server-side (a rep of any other type is refused; an unknown rep is not found).
/// </summary>
public class AreaRolesHandlerTests
{
    private static Representative Rep(RepresentativeType type) =>
        Representative.Register($"{type.Name} Rep", type, GoalDuration.Monthly, Money.Zero, Money.Zero);

    [Fact]
    public async Task Create_assigns_a_manager_and_a_responsible_of_the_matching_types()
    {
        var mgr = Rep(RepresentativeType.AreaManager);
        var resp = Rep(RepresentativeType.AreaResponsible);
        var reps = new FakeRepresentativeRepository(); reps.Store.Add(mgr); reps.Store.Add(resp);
        var areas = new FakeAreaRepository();
        var handler = new CreateAreaHandler(areas, reps, new FakeCurrentUser());

        var id = await handler.Handle(new CreateAreaCommand("Nasr City", Guid.NewGuid(), false, Array.Empty<Guid>(),
            AreaManagerId: mgr.Id.Value, AreaResponsibleId: resp.Id.Value), CancellationToken.None);

        var area = areas.Store.Single(a => a.Id.Value == id);
        area.AreaManagerId.Should().Be(mgr.Id);
        area.AreaResponsibleId.Should().Be(resp.Id);
    }

    [Fact]
    public async Task Create_rejects_a_manager_whose_type_is_not_AreaManager()
    {
        var collector = Rep(RepresentativeType.Collector);
        var reps = new FakeRepresentativeRepository(); reps.Store.Add(collector);
        var areas = new FakeAreaRepository();
        var handler = new CreateAreaHandler(areas, reps, new FakeCurrentUser());

        var act = () => handler.Handle(new CreateAreaCommand("Nasr City", Guid.NewGuid(), false, Array.Empty<Guid>(),
            AreaManagerId: collector.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<FollowUp.Application.Common.Exceptions.ValidationException>();
        areas.Store.Should().BeEmpty("nothing is created when the assignment is invalid");
    }

    [Fact]
    public async Task Create_rejects_an_unknown_responsible()
    {
        var handler = new CreateAreaHandler(new FakeAreaRepository(), new FakeRepresentativeRepository(), new FakeCurrentUser());

        var act = () => handler.Handle(new CreateAreaCommand("Nasr City", Guid.NewGuid(), false, Array.Empty<Guid>(),
            AreaResponsibleId: Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_reassigns_a_role_and_clears_the_other()
    {
        var mgr1 = Rep(RepresentativeType.AreaManager);
        var mgr2 = Rep(RepresentativeType.AreaManager);
        var resp = Rep(RepresentativeType.AreaResponsible);
        var reps = new FakeRepresentativeRepository(); reps.Store.Add(mgr1); reps.Store.Add(mgr2); reps.Store.Add(resp);
        var cityId = CityId.New();
        var area = Area.Create("Nasr City", cityId, false);
        area.AssignManager(mgr1.Id); area.AssignResponsible(resp.Id);
        var areas = new FakeAreaRepository(); areas.Store.Add(area);
        var handler = new UpdateAreaHandler(areas, reps, new FakeCurrentUser());

        await handler.Handle(new UpdateAreaCommand(area.Id.Value, "Nasr City", cityId.Value, false,
            AreaManagerId: mgr2.Id.Value, AreaResponsibleId: null), CancellationToken.None);

        area.AreaManagerId.Should().Be(mgr2.Id, "the manager was reassigned");
        area.AreaResponsibleId.Should().BeNull("the responsible was cleared");
    }

    [Fact]
    public async Task Update_rejects_a_responsible_whose_type_is_not_AreaResponsible()
    {
        var mgr = Rep(RepresentativeType.AreaManager);
        var reps = new FakeRepresentativeRepository(); reps.Store.Add(mgr);
        var cityId = CityId.New();
        var area = Area.Create("Nasr City", cityId, false);
        var areas = new FakeAreaRepository(); areas.Store.Add(area);
        var handler = new UpdateAreaHandler(areas, reps, new FakeCurrentUser());

        // An AreaManager offered as the responsible must be refused.
        var act = () => handler.Handle(new UpdateAreaCommand(area.Id.Value, "Nasr City", cityId.Value, false,
            AreaResponsibleId: mgr.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<FollowUp.Application.Common.Exceptions.ValidationException>();
        area.AreaResponsibleId.Should().BeNull();
    }
}
