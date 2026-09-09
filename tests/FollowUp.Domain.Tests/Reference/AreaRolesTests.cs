using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using Xunit;

namespace FollowUp.Domain.Tests.Reference;

/// <summary>Area management roles: the two new rep types and an area's manager/responsible assignment.</summary>
public class AreaRolesTests
{
    [Fact]
    public void Representative_type_gains_area_responsible_and_area_manager()
    {
        Enumeration.GetAll<RepresentativeType>().Should().HaveCount(6);
        Enumeration.FromName<RepresentativeType>("AreaResponsible").Should().BeSameAs(RepresentativeType.AreaResponsible);
        Enumeration.FromName<RepresentativeType>("AreaManager").Should().BeSameAs(RepresentativeType.AreaManager);
        // Stable ids — persisted by Name, but the ids must never collide with the existing four.
        RepresentativeType.AreaResponsible.Id.Should().Be(5);
        RepresentativeType.AreaManager.Id.Should().Be(6);
    }

    [Fact]
    public void An_area_can_be_assigned_and_cleared_a_manager_and_a_responsible()
    {
        var area = Area.Create("Nasr City", CityId.New(), transportationRequired: false);
        area.AreaManagerId.Should().BeNull("unassigned by default");
        area.AreaResponsibleId.Should().BeNull();

        var manager = RepresentativeId.New();
        var responsible = RepresentativeId.New();
        area.AssignManager(manager);
        area.AssignResponsible(responsible);
        area.AreaManagerId.Should().Be(manager);
        area.AreaResponsibleId.Should().Be(responsible);

        area.AssignManager(null);
        area.AreaManagerId.Should().BeNull("assignment can be cleared");
        area.AreaResponsibleId.Should().Be(responsible, "clearing one role leaves the other untouched");
    }

    [Fact]
    public void Oracle_mirror_update_never_touches_the_operator_assigned_roles()
    {
        // The roles are operator-managed (like RealName); the Oracle sync only mirrors name/city.
        var area = Area.FromOracle("A-1", "Old Name", CityId.New());
        var manager = RepresentativeId.New();
        area.AssignManager(manager);

        area.ApplyOracle("New Name", CityId.New());

        area.Name.Should().Be("New Name");
        area.AreaManagerId.Should().Be(manager, "the sync must not clear an operator's role assignment");
    }
}
