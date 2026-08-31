using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Signatures;

namespace FollowUp.Domain.Tests.Signatures;

/// <summary>
/// SIG-10: the signature "module" was a free string validated only for non-emptiness. It is now a closed set
/// (SignableModule), enforced in the aggregate factory so an out-of-set module cannot be signed.
/// </summary>
public class SignableModuleTests
{
    [Fact]
    public void Only_the_known_modules_are_accepted()
    {
        SignableModule.IsKnown("complaint").Should().BeTrue();
        SignableModule.IsKnown("Complaint").Should().BeTrue(); // case-insensitive
        SignableModule.IsKnown("laboratory").Should().BeFalse();
        SignableModule.IsKnown("").Should().BeFalse();
        SignableModule.IsKnown(null).Should().BeFalse();
    }

    [Fact]
    public void Create_rejects_an_unknown_module()
    {
        var act = () => ElectronicSignature.Create("bogus", "rec-1", 1u, Guid.NewGuid(), "user", "password",
            SignatureMeaning.Approval, null, "hash", DateTimeOffset.UnixEpoch, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_accepts_the_complaint_module()
    {
        var sig = ElectronicSignature.Create(SignableModule.Complaint, "rec-1", 1u, Guid.NewGuid(), "user", "password",
            SignatureMeaning.Approval, null, "hash", DateTimeOffset.UnixEpoch, null);

        sig.Module.Should().Be("complaint");
    }
}
