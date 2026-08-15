using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using FollowUp.Domain.Statistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal sealed class MonthlySampleConfiguration : IEntityTypeConfiguration<MonthlySample>
{
    public void Configure(EntityTypeBuilder<MonthlySample> b)
    {
        b.ToTable("monthly_sample");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.Period);
        b.Property(x => x.SampleCount);
        b.HasOne<Laboratory>().WithMany().HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Representative>().WithMany().HasForeignKey(x => x.CollectorRepId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.LaboratoryId, x.Period }).IsUnique();
    }
}

internal sealed class DailyLabStatisticConfiguration : IEntityTypeConfiguration<DailyLabStatistic>
{
    public void Configure(EntityTypeBuilder<DailyLabStatistic> b)
    {
        b.ToTable("daily_lab_statistic");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.Date);
        b.Property(x => x.LabCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.Registrations);
        b.Property(x => x.TestCount);
        b.Property(x => x.Income); // Money -> numeric(18,2) via convention
        b.HasIndex(x => new { x.Date, x.LabCode }).IsUnique();
    }
}

internal sealed class TestStatisticConfiguration : IEntityTypeConfiguration<TestStatistic>
{
    public void Configure(EntityTypeBuilder<TestStatistic> b)
    {
        b.ToTable("test_statistic");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.Date);
        b.Property(x => x.TestCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.Count);
        b.HasIndex(x => new { x.Date, x.TestCode }).IsUnique();
    }
}

internal sealed class TestGroupConfiguration : IEntityTypeConfiguration<TestGroup>
{
    public void Configure(EntityTypeBuilder<TestGroup> b)
    {
        b.ToTable("test_group");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.Code).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(200);
    }
}

internal sealed class TestSetupConfiguration : IEntityTypeConfiguration<TestSetup>
{
    public void Configure(EntityTypeBuilder<TestSetup> b)
    {
        b.ToTable("test_setup");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.Code).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(200);

        // test_setup -> test_group is SET NULL on delete (SRS FR-14).
        b.HasOne<TestGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.GroupId);
    }
}
