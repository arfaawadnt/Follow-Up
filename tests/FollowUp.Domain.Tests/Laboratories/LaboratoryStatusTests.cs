using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Domain.Tests.Laboratories;

public class LaboratoryStatusTests
{
    [Fact]
    public void Schedulable_set_is_the_single_source_of_truth_for_IsSchedulable()
    {
        // BRD-4: board generation and intra-day reconcile must read the same set; locking membership here means
        // adding a schedulable status is a one-line change that both paths pick up.
        LaboratoryStatus.Schedulable.Should().BeEquivalentTo(new[]
        {
            LaboratoryStatus.Active, LaboratoryStatus.Pending, LaboratoryStatus.Interactive,
        });

        foreach (var status in Enumeration.GetAll<LaboratoryStatus>())
            status.IsSchedulable.Should().Be(LaboratoryStatus.Schedulable.Contains(status));
    }
}
