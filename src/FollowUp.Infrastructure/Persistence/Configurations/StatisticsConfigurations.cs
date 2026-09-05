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
        b.Property(x => x.TestType).HasDefaultValue(0);
        b.Property(x => x.Count);
        b.Property(x => x.Income); // Money -> numeric(18,2) via convention
        // Natural key is (date, code, type): GLOBAL_TESTS2 reuses a test_code across test_types.
        b.HasIndex(x => new { x.Date, x.TestCode, x.TestType }).IsUnique();
    }
}

internal sealed class DetailedRegistrationConfiguration : IEntityTypeConfiguration<DetailedRegistration>
{
    public void Configure(EntityTypeBuilder<DetailedRegistration> b)
    {
        b.ToTable("detailed_registration");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.Date);
        b.Property(x => x.LabCode).HasMaxLength(32);
        b.Property(x => x.RegBranchCode).HasMaxLength(32);
        b.Property(x => x.AccNo).HasMaxLength(64).IsRequired();
        b.Property(x => x.PatientName).HasMaxLength(256).IsRequired();
        b.Property(x => x.TestCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.TestType);
        b.Property(x => x.TestName).HasMaxLength(256);
        b.Property(x => x.PatientFee).HasColumnType("numeric(18,2)");
        b.Property(x => x.InsuranceFee).HasColumnType("numeric(18,2)");
        b.Property(x => x.SampleStatus).HasMaxLength(64);
        b.Property(x => x.TestStatus).HasMaxLength(64);
        b.Ignore(x => x.Fee); // computed (PatientFee + InsuranceFee)
        // Window-replace sync + range reads: index by date (and lab code for the scoped/grouped read).
        b.HasIndex(x => new { x.Date, x.LabCode });
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
        b.Property(x => x.Source).HasDefaultValue(CatalogueSource.Manual);
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
        b.Property(x => x.TestType).HasDefaultValue(0);
        // Natural key is (code, type): Oracle's GLOBAL_TESTS2 allows the same test_code across test_types.
        b.HasIndex(x => new { x.Code, x.TestType }).IsUnique();
        b.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(200);
        b.Property(x => x.Cost); // Money -> numeric(18,2) via convention
        b.Property(x => x.Source).HasDefaultValue(CatalogueSource.Manual);

        // test_setup -> test_group is SET NULL on delete (SRS FR-14).
        b.HasOne<TestGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.GroupId);
    }
}
