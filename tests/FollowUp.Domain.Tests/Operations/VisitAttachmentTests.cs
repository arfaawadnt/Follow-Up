using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;

namespace FollowUp.Domain.Tests.Operations;

/// <summary>
/// Finding M-1 / OPS-003: a pending attachment binds once, to the visit it was recorded with. Re-pointing an
/// already-bound attachment at a different visit/lab must be refused, so a foreign attachment can never be
/// rewritten into another visit's scope.
/// </summary>
public class VisitAttachmentTests
{
    private static VisitAttachment Pending() =>
        VisitAttachment.CreatePending("stored.bin", "scan.pdf", "application/pdf", 1234);

    [Fact]
    public void BindTo_sets_the_owning_visit_and_lab()
    {
        var a = Pending();
        var visit = Guid.NewGuid();
        var lab = LaboratoryId.New();

        a.BindTo(visit, lab);

        a.VisitId.Should().Be(visit);
        a.LaboratoryId.Should().Be(lab);
    }

    [Fact]
    public void Rebinding_to_a_different_visit_is_refused()
    {
        var a = Pending();
        a.BindTo(Guid.NewGuid(), LaboratoryId.New());

        var act = () => a.BindTo(Guid.NewGuid(), LaboratoryId.New());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Rebinding_to_the_same_visit_is_idempotent()
    {
        var a = Pending();
        var visit = Guid.NewGuid();
        var lab = LaboratoryId.New();
        a.BindTo(visit, lab);

        var act = () => a.BindTo(visit, lab);

        act.Should().NotThrow();
    }
}
