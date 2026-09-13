using FluentAssertions;
using FollowUp.Domain.Laboratories;
using Xunit;

namespace FollowUp.Domain.Tests.Laboratories;

/// <summary>
/// Laboratory "Credit": an operator-managed flag (the lab settles on credit), unchecked by default and never touched
/// by the Oracle mirror — it is deliberately absent from ApplyOracleMaster, exactly like the serving Branch.
/// </summary>
public class LabCreditTests
{
    [Fact]
    public void A_new_lab_is_not_on_credit_by_default()
    {
        var lab = Laboratory.Register(LabCode.Create("MGL-1"), "Lab", "B");
        lab.Credit.Should().BeFalse("unchecked by default");
    }

    [Fact]
    public void Credit_can_be_switched_on_and_off()
    {
        var lab = Laboratory.Register(LabCode.Create("MGL-2"), "Lab", "B");
        lab.SetCredit(true);
        lab.Credit.Should().BeTrue();
        lab.SetCredit(false);
        lab.Credit.Should().BeFalse();
    }

    [Fact]
    public void Oracle_master_update_never_touches_the_operator_managed_credit_flag()
    {
        var lab = Laboratory.FromOracle(LabCode.Create("MGL-3"), "Old Name");
        lab.SetCredit(true);

        // The mirror rewrites every Oracle-owned master field; Credit is not one of them.
        lab.ApplyOracleMaster("New Name", "Cat", null, "Cairo", "Nasr", "Area 1", "Addr", null);

        lab.Name.Should().Be("New Name", "the sync still mirrors Oracle-owned master data");
        lab.Credit.Should().BeTrue("the sync must not clear an operator's Credit flag");
    }
}
