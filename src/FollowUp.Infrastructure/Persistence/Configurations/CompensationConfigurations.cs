using System.Text.Json;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal sealed class LabLoyaltyLedgerConfiguration : IEntityTypeConfiguration<LabLoyaltyLedger>
{
    public void Configure(EntityTypeBuilder<LabLoyaltyLedger> b)
    {
        b.ToTable("lab_loyalty_ledger");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.Period);
        b.Property(x => x.Target);
        b.Property(x => x.Achieved);
        b.Property(x => x.Points);
        b.Property(x => x.Tier).HasMaxLength(32);
        b.Property(x => x.ComputedAt);

        b.HasOne<Laboratory>().WithMany().HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.LaboratoryId, x.Period }).IsUnique();
    }
}

internal sealed class RepCommissionConfiguration : IEntityTypeConfiguration<RepCommission>
{
    public void Configure(EntityTypeBuilder<RepCommission> b)
    {
        b.ToTable("rep_commission");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.Ignore(x => x.Total); // derived = base + commission + bonus

        b.Property(x => x.Period);
        b.Property(x => x.Target).HasPrecision(18, 2);
        b.Property(x => x.Achieved).HasPrecision(18, 2);
        b.Property(x => x.BaseSalary);
        b.Property(x => x.Commission);
        b.Property(x => x.Bonus);
        b.Property(x => x.ComputedAt);

        b.HasOne<Representative>().WithMany().HasForeignKey(x => x.RepresentativeId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.RepresentativeId, x.Period }).IsUnique();
    }
}

file sealed record TierSurrogate(string Name, decimal MinAchievementPercent, int Points);

internal sealed class CompensationConfigConfiguration : IEntityTypeConfiguration<CompensationConfig>
{
    public void Configure(EntityTypeBuilder<CompensationConfig> b)
    {
        b.ToTable("compensation_config");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(50);
        b.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid"); // xmin optimistic concurrency (CPN-9)
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.CommissionRatePercent).HasPrecision(9, 4);
        b.Property(x => x.BonusThresholdPercent).HasPrecision(9, 4);
        b.Property(x => x.BonusAmount);

        var comparer = new ValueComparer<IReadOnlyList<LoyaltyTier>>(
            (a, b2) => a!.SequenceEqual(b2!),
            v => v.Aggregate(0, (h, x) => h ^ x.GetHashCode()),
            v => v.ToList());

        b.Property(x => x.LoyaltyTiers)
            .HasColumnName("loyalty_tiers")
            .HasColumnType("jsonb")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => JsonSerializer.Serialize(v.Select(t => new TierSurrogate(t.Name, t.MinAchievementPercent, t.Points)), (JsonSerializerOptions?)null),
                s => JsonSerializer.Deserialize<List<TierSurrogate>>(s, (JsonSerializerOptions?)null)!
                        .Select(t => new LoyaltyTier(t.Name, t.MinAchievementPercent, t.Points)).ToList(),
                comparer);
    }
}
