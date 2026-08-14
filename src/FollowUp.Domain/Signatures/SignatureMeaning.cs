using FollowUp.Domain.Common;

namespace FollowUp.Domain.Signatures;

/// <summary>
/// The declared intent bound into an electronic signature (SRS FR-19, signature meaning CHECK = 5).
/// </summary>
public sealed class SignatureMeaning : Enumeration
{
    public static readonly SignatureMeaning Authorship = new(1, nameof(Authorship));
    public static readonly SignatureMeaning Review = new(2, nameof(Review));
    public static readonly SignatureMeaning Approval = new(3, nameof(Approval));
    public static readonly SignatureMeaning Verification = new(4, nameof(Verification));
    public static readonly SignatureMeaning Execution = new(5, nameof(Execution));

    private SignatureMeaning(int id, string name) : base(id, name) { }
}
