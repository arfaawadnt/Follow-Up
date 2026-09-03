using FluentAssertions;
using FollowUp.Application.Features.Complaints.Commands;

namespace FollowUp.Application.Tests.Features.Complaints;

/// <summary>CMP-19: validators must mirror the schema's varchar bounds (over-length → 400, not a 22001 → 500)
/// and reject a future received date.</summary>
public class LogComplaintValidatorTests
{
    private static readonly LogComplaintValidator Validator = new();

    private static LogComplaintCommand Valid() => new()
    {
        LaboratoryId = Guid.NewGuid(),
        Category = "Result Quality",
        ViaChannel = "Phone",
        Details = "details",
    };

    [Fact]
    public void Accepts_a_well_formed_complaint() =>
        Validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Rejects_an_over_length_category() =>
        Validator.Validate(Valid() with { Category = new string('x', 101) }).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_an_over_length_assigned_team() =>
        Validator.Validate(Valid() with { AssignedTeam = new string('x', 101) }).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_a_future_received_date() =>
        Validator.Validate(Valid() with { ReceivedAt = DateTimeOffset.UtcNow.AddYears(1) }).IsValid.Should().BeFalse();
}
