using FollowUp.Domain.Common;

namespace FollowUp.Domain.Signatures;

/// <summary>
/// The declared meaning of an electronic signature (SRS FR-19, signature meaning CHECK = 5). This value also
/// carries the standard's separate "Intent" element (314): intent is realized through Meaning selection plus the
/// re-authentication signing ceremony rather than a distinct stored field — see docs/adr/0010 (finding SIG-14).
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
