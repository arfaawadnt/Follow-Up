using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using Xunit;

namespace FollowUp.Domain.Tests.Reference;

/// <summary>
/// Management roles: the rep types AreaManager / LabResponsible, an area's manager assignment and a lab's responsible
/// assignment (the responsible moved from the area to the lab on 2026-09-16).
/// </summary>
public class AreaRolesTests
{
    [Fact]
    public void Representative_type_has_lab_responsible_and_area_manager()
    {
        Enumeration.GetAll<RepresentativeType>().Should().HaveCount(6);
        Enumeration.FromName<RepresentativeType>("LabResponsible").Should().BeSameAs(RepresentativeType.LabResponsible);
        Enumeration.FromName<RepresentativeType>("AreaManager").Should().BeSameAs(RepresentativeType.AreaManager);
        // Stable ids — persisted by Name, but the ids must never collide with the existing four. LabResponsible keeps
        // the id of the AreaResponsible it replaced (rows are renamed by migration).
        RepresentativeType.LabResponsible.Id.Should().Be(5);
        RepresentativeType.AreaManager.Id.Should().Be(6);
        FluentActions.Invoking(() => Enumeration.FromName<RepresentativeType>("AreaResponsible")).Should().Throw<Exception>("the old name is gone");
    }

    [Fact]
    public void An_area_can_be_assigned_and_cleared_a_manager()
    {
        var area = Area.Create("Nasr City", CityId.New(), transportationRequired: false);
        area.AreaManagerId.Should().BeNull("unassigned by default");

        var manager = RepresentativeId.New();
        area.AssignManager(manager);
        area.AreaManagerId.Should().Be(manager);

        area.AssignManager(null);
        area.AreaManagerId.Should().BeNull("assignment can be cleared");
    }

    [Fact]
    public void A_lab_can_be_assigned_and_cleared_a_responsible_and_the_oracle_sync_never_touches_it()
    {
        var lab = Laboratory.Register(LabCode.Create("MGL-7001"), "Alpha Lab", "B");
        lab.ResponsibleRepId.Should().BeNull("unassigned by default");

        var responsible = RepresentativeId.New();
        lab.AssignResponsible(responsible);
        lab.ResponsibleRepId.Should().Be(responsible);

        // Operator-managed like the marketing rep: the Oracle master sync rewrites the descriptive fields only.
        lab.ApplyOracleMaster("Alpha Lab (Oracle)", "Cat", "BR-1", "Cairo", null, null, null, null);
        lab.ResponsibleRepId.Should().Be(responsible, "the sync must not clear an operator's assignment");

        lab.AssignResponsible(null);
        lab.ResponsibleRepId.Should().BeNull("assignment can be cleared");
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
