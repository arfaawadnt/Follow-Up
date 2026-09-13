using FollowUp.Domain.Accounting;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

// Accounting module. Money columns take numeric(18,2) and Enumerations persist by Name via the DbContext conventions.
// "Serial" is a PostgreSQL identity (GENERATED ALWAYS) so every ledger row gets a stable human-facing sequence number.
// Every FK to a scoped aggregate is Restrict: a lab / area / rep / treasury / reason that has money rows cannot be hard-
// deleted out from under them (labs and reps are only ever deactivated anyway).

internal sealed class TreasuryReasonConfiguration : IEntityTypeConfiguration<TreasuryReason>
{
    public void Configure(EntityTypeBuilder<TreasuryReason> b)
    {
        b.ToTable("treasury_reason");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.IsActive);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class TreasuryConfiguration : IEntityTypeConfiguration<Treasury>
{
    public void Configure(EntityTypeBuilder<Treasury> b)
    {
        b.ToTable("treasury");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.IsActive);
        b.Property(x => x.Branches)
            .HasColumnName("branches").HasColumnType("jsonb")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion<StringListConverter>(new StringListComparer());
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class TreasuryEntryConfiguration : IEntityTypeConfiguration<TreasuryEntry>
{
    public void Configure(EntityTypeBuilder<TreasuryEntry> b)
    {
        b.ToTable("treasury_entry");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Serial).UseIdentityAlwaysColumn();
        b.Property(x => x.Date);
        b.Property(x => x.Debit);
        b.Property(x => x.Credit);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasOne<Treasury>().WithMany().HasForeignKey(x => x.TreasuryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TreasuryReason>().WithMany().HasForeignKey(x => x.ReasonId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TreasuryId, x.Date });
        b.HasIndex(x => x.Serial).IsUnique();
    }
}

internal sealed class PenaltyRecordConfiguration : IEntityTypeConfiguration<PenaltyRecord>
{
    public void Configure(EntityTypeBuilder<PenaltyRecord> b)
    {
        b.ToTable("penalty_record");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Serial).UseIdentityAlwaysColumn();
        b.Property(x => x.Date);
        b.Property(x => x.AccNo).HasMaxLength(50).IsRequired();
        b.Property(x => x.PatientName).HasMaxLength(200).IsRequired();
        b.Property(x => x.WrongTestCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.WrongTestName).HasMaxLength(200).IsRequired();
        b.Property(x => x.WrongValue);
        b.Property(x => x.RightTestCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.RightTestName).HasMaxLength(200).IsRequired();
        b.Property(x => x.RightValue);
        b.Property(x => x.User).HasColumnName("penalty_user"); // "user" is a reserved word in PostgreSQL
        b.Ignore(x => x.PenaltyAmount); // derived: wrong − right
        b.HasOne<Laboratory>().WithMany().HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.LaboratoryId, x.Date });
        b.HasIndex(x => x.Date);
        b.HasIndex(x => x.Serial).IsUnique();
    }
}

internal sealed class DeductionConfiguration : IEntityTypeConfiguration<Deduction>
{
    public void Configure(EntityTypeBuilder<Deduction> b)
    {
        b.ToTable("deduction");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Serial).UseIdentityAlwaysColumn();
        b.Property(x => x.Date);
        b.Property(x => x.Value);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.PeriodFrom);
        b.Property(x => x.PeriodTo);
        b.HasOne<Area>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AreaId, x.Date });
        b.HasIndex(x => x.Serial).IsUnique();
    }
}

internal sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> b)
    {
        b.ToTable("collection");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Serial).UseIdentityAlwaysColumn();
        b.Property(x => x.Date);
        b.Property(x => x.Cash);
        b.Property(x => x.Bank);
        b.Property(x => x.DoneBy).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Ignore(x => x.Total); // derived: cash + bank
        b.Property(x => x.RepIds)
            .HasColumnName("rep_ids").HasColumnType("jsonb")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion<RepIdListConverter>(new RepIdListComparer());
        b.HasOne<Laboratory>().WithMany().HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.LaboratoryId, x.Date });
        b.HasIndex(x => x.Date);
        b.HasIndex(x => x.Serial).IsUnique();
    }
}

internal sealed class RepIncomeEntryConfiguration : IEntityTypeConfiguration<RepIncomeEntry>
{
    public void Configure(EntityTypeBuilder<RepIncomeEntry> b)
    {
        b.ToTable("rep_income_entry");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.Serial).UseIdentityAlwaysColumn();
        b.Property(x => x.Date);
        b.Property(x => x.Amount);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasOne<Representative>().WithMany().HasForeignKey(x => x.RepresentativeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.RepresentativeId, x.Date });
        b.HasIndex(x => x.Serial).IsUnique();
    }
}
