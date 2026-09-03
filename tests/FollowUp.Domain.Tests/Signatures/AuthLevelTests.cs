using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Signatures;

namespace FollowUp.Domain.Tests.Signatures;

/// <summary>
/// SIG-11: the signature auth_level was the hardcoded magic string "password". It is now a closed set
/// (SignatureAuthLevel) enforced in the aggregate factory, so an unknown assurance level cannot be recorded.
/// </summary>
public class AuthLevelTests
{
    [Fact]
    public void Only_known_auth_levels_are_accepted()
    {
        SignatureAuthLevel.IsKnown("password").Should().BeTrue();
        SignatureAuthLevel.IsKnown("PASSWORD").Should().BeTrue(); // case-insensitive
        SignatureAuthLevel.IsKnown("mfa").Should().BeFalse();
        SignatureAuthLevel.IsKnown(null).Should().BeFalse();
    }

    [Fact]
    public void Create_rejects_an_unknown_auth_level()
    {
        var act = () => ElectronicSignature.Create(SignableModule.Complaint, "rec-1", 1u, Guid.NewGuid(), "user",
            "bogus-level", SignatureMeaning.Approval, null, "hash", DateTimeOffset.UnixEpoch, null);

        act.Should().Throw<DomainException>();
    }
}
