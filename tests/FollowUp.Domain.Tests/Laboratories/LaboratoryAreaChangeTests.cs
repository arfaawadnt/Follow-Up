using FluentAssertions;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Domain.Tests.Laboratories;

public class LaboratoryAreaChangeTests
{
    private static Laboratory Lab() => Laboratory.Register(LabCode.Create("MGL-1"), "Lab", "B");

    [Fact]
    public void Changing_the_area_raises_the_event_with_old_and_new()
    {
        var lab = Lab();
        lab.PlaceInHierarchy("Main", "Giza", "Dokki", "Dokki");

        lab.PlaceInHierarchy("Main", "Cairo", "Nasr City", "Zone-1");

        var events = lab.DomainEvents.OfType<LaboratoryAreaChanged>().ToList();
        events.Should().HaveCount(2); // initial placement (null -> Dokki) + the move
        events[^1].OldArea.Should().Be("Dokki");
        events[^1].NewArea.Should().Be("Zone-1");
    }

    [Fact]
    public void Rehoming_without_an_area_change_raises_nothing_extra()
    {
        var lab = Lab();
        lab.PlaceInHierarchy("Main", "Giza", "Dokki", "Dokki");

        lab.PlaceInHierarchy("Branch-2", "Cairo", "Maadi", "Dokki"); // same area, other fields move

        lab.DomainEvents.OfType<LaboratoryAreaChanged>().Should().HaveCount(1); // only the initial placement
    }

    [Fact]
    public void Clearing_the_area_raises_with_a_null_new_area()
    {
        var lab = Lab();
        lab.PlaceInHierarchy("Main", "Giza", "Dokki", "Dokki");

        lab.PlaceInHierarchy("Main", "Giza", "Dokki", null);

        var last = lab.DomainEvents.OfType<LaboratoryAreaChanged>().Last();
        last.OldArea.Should().Be("Dokki");
        last.NewArea.Should().BeNull();
    }

    [Fact]
    public void Hierarchy_values_are_trimmed_and_blank_becomes_null()
    {
        var lab = Lab();

        lab.PlaceInHierarchy(" Main ", "  ", "Dokki", " Zone-1 ");

        lab.Branch.Should().Be("Main");
        lab.Governorate.Should().BeNull();
        lab.Area.Should().Be("Zone-1"); // tracking rows key on the exact Area string
        lab.DomainEvents.OfType<LaboratoryAreaChanged>().Last().NewArea.Should().Be("Zone-1");

        lab.PlaceInHierarchy("Main", null, "Dokki", "Zone-1  ");
        lab.DomainEvents.OfType<LaboratoryAreaChanged>().Should().HaveCount(1); // trimmed-equal — no event
    }

    [Fact]
    public void Area_changed_event_round_trips_through_outbox_json()
    {
        var evt = new LaboratoryAreaChanged(LaboratoryId.New(), null, "Zone-1");

        // Mirrors AuditAndOutboxInterceptor (serialize by concrete type) + OutboxDispatcher (deserialize by resolved type).
        var json = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType());
        var back = (LaboratoryAreaChanged?)System.Text.Json.JsonSerializer.Deserialize(json, typeof(LaboratoryAreaChanged));

        back.Should().NotBeNull();
        back!.LaboratoryId.Should().Be(evt.LaboratoryId);
        back.OldArea.Should().BeNull();
        back.NewArea.Should().Be("Zone-1");
    }
}
