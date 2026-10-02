using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Statistics;
using Xunit;

namespace FollowUp.Domain.Tests.Statistics;

/// <summary>2026-09-30: a synced LDM registration edit keeps its wall-clock stamps and derives the delay after the registration.</summary>
public sealed class RegistrationChangeTests
{
    private static readonly DateTime Reg = new(2026, 9, 27, 9, 0, 0);

    [Fact]
    public void Create_normalises_the_fields_and_derives_the_delay()
    {
        var c = RegistrationChange.Create(13919, 20000000908757, " 91967763 ", " عبد الرحيم رجب ", Reg, new DateOnly(2026, 9, 27), " 1 ", " mgl-alpha ",
            " Patient Name ", " old ", " new ", " Mohamed Samir ", Reg.AddHours(2).AddMinutes(30));
        c.TransId.Should().Be(13919); c.RegKey.Should().Be(20000000908757);
        c.AccNo.Should().Be("91967763"); c.PatientName.Should().Be("عبد الرحيم رجب");
        c.LabCode.Should().Be("MGL-ALPHA"); c.RegBranchCode.Should().Be("1");
        c.Column.Should().Be("Patient Name"); c.OldValue.Should().Be("old"); c.NewValue.Should().Be("new"); c.ModifiedBy.Should().Be("Mohamed Samir");
        c.ModifiedDate.Should().Be(new DateOnly(2026, 9, 27));
        c.DelayFromCreation.Should().Be(TimeSpan.FromMinutes(150));
        c.RegCreatedAt!.Value.Kind.Should().Be(DateTimeKind.Unspecified); c.ModifiedAt.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public void Unknown_creation_time_or_lab_stay_null_and_blanks_fall_back()
    {
        var c = RegistrationChange.Create(1, 2, null, null, null, null, "  ", null, "", "", "", null, Reg);
        c.DelayFromCreation.Should().BeNull(); c.LabCode.Should().BeNull(); c.RegBranchCode.Should().BeNull(); c.RegDate.Should().BeNull();
        c.AccNo.Should().Be(""); c.Column.Should().Be("—"); c.ModifiedBy.Should().Be("—"); c.OldValue.Should().BeNull(); c.NewValue.Should().BeNull();
    }

    [Fact]
    public void The_ldm_transaction_id_is_required()
    {
        var act = () => RegistrationChange.Create(0, 2, "A", "P", null, null, null, null, "X", null, null, "u", Reg);
        act.Should().Throw<DomainException>();
    }
}
