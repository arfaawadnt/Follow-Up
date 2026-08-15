using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;

namespace FollowUp.Domain.Statistics;

public readonly record struct MonthlySampleId(Guid Value)
{
    public static MonthlySampleId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Accumulated verified sample volume for a lab in a month (Workflows §4/§9). The midnight roll-over rolls
/// verified, received visits into this total, which then feeds the loyalty and commission engines.
/// One row per (lab, year-month).
/// </summary>
public sealed class MonthlySample : AggregateRoot<MonthlySampleId>
{
    private MonthlySample() { } // EF

    private MonthlySample(MonthlySampleId id, LaboratoryId labId, RepresentativeId? collectorRepId, YearMonth period)
        : base(id)
    {
        LaboratoryId = labId;
        CollectorRepId = collectorRepId;
        Period = period;
    }

    public LaboratoryId LaboratoryId { get; private set; }
    public RepresentativeId? CollectorRepId { get; private set; }
    public YearMonth Period { get; private set; }
    public int SampleCount { get; private set; }

    public static MonthlySample Start(LaboratoryId labId, RepresentativeId? collectorRepId, YearMonth period) =>
        new(MonthlySampleId.New(), labId, collectorRepId, period);

    /// <summary>Adds verified samples to the monthly total (idempotency is enforced by the caller/roll-over).</summary>
    public void Add(int samples)
    {
        if (samples < 0) throw new DomainException("Cannot add a negative sample count.");
        SampleCount += samples;
    }
}
