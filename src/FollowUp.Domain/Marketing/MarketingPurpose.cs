using FollowUp.Domain.Common;

namespace FollowUp.Domain.Marketing;

/// <summary>The seven purposes a marketing visit may serve (SRS FR-10).</summary>
public sealed class MarketingPurpose : Enumeration
{
    public static readonly MarketingPurpose Pitch = new(1, nameof(Pitch));
    public static readonly MarketingPurpose Renewal = new(2, nameof(Renewal));
    public static readonly MarketingPurpose ComplaintResolution = new(3, "ComplaintResolution");
    public static readonly MarketingPurpose Promotion = new(4, nameof(Promotion));
    public static readonly MarketingPurpose Onboarding = new(5, nameof(Onboarding));
    public static readonly MarketingPurpose Reactivation = new(6, nameof(Reactivation));
    public static readonly MarketingPurpose Routine = new(7, nameof(Routine));

    private MarketingPurpose(int id, string name) : base(id, name) { }
}
