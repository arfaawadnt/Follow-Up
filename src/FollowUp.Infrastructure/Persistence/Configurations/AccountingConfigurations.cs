using FollowUp.Domain.Accounting;
using FollowUp.Domain.Identity;
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
        // Collection mirroring + validation (2026-09-16). ReasonId became optional: a mirrored collection has no reason row.
        b.Property(x => x.ReasonId).IsRequired(false);
        b.Property(x => x.Origin).HasDefaultValue(TreasuryEntryOrigin.Manual);
        b.Property(x => x.CollectionId);
        b.Property(x => x.CollectedCash);
        b.Property(x => x.SystemNote).HasMaxLength(1000);
        b.Property(x => x.ValidationStatus).HasDefaultValue(TreasuryValidationStatus.NotRequired);
        b.Property(x => x.ValidatedAt);
        b.Property(x => x.ValidatedBy).HasMaxLength(100);
        b.Property(x => x.ValidationNote).HasMaxLength(500);
        b.Ignore(x => x.HasDiscrepancy);
        b.HasOne<Treasury>().WithMany().HasForeignKey(x => x.TreasuryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TreasuryReason>().WithMany().HasForeignKey(x => x.ReasonId).OnDelete(DeleteBehavior.Restrict);
        // Restrict: the collection command removes (or refuses to remove) its mirror first, so a dangling mirror can never remain.
        b.HasOne<Collection>().WithMany().HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CollectionId).IsUnique().HasFilter("collection_id IS NOT NULL");
        b.HasIndex(x => new { x.TreasuryId, x.Date });
        b.HasIndex(x => new { x.TreasuryId, x.ValidationStatus });
        b.HasIndex(x => x.Serial).IsUnique();
    }
}

internal sealed class TreasuryGrantConfiguration : IEntityTypeConfiguration<TreasuryGrant>
{
    public void Configure(EntityTypeBuilder<TreasuryGrant> b)
    {
        b.ToTable("treasury_grant");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();
        b.Property(x => x.CanView);
        b.Property(x => x.CanValidate);
        b.Property(x => x.CanUpdate);
        b.Ignore(x => x.IsEmpty);
        b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);        // a deleted role takes its grants
        b.HasOne<Treasury>().WithMany().HasForeignKey(x => x.TreasuryId).OnDelete(DeleteBehavior.Restrict); // treasuries are never hard-deleted
        b.HasIndex(x => new { x.RoleId, x.TreasuryId }).IsUnique();
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
        b.Property(x => x.UserType).HasColumnName("penalty_user"); // "user" is a reserved word in PostgreSQL
        // "Performed by": exactly one of the two, matching the user type (domain invariant + ck_penalty_record_performed_by).
        // Restrict, like every accounting reference: users and reps are deactivated, never hard-deleted, so the row's
        // attribution can never dangle.
        b.Property(x => x.PerformedByUserId);
        b.Property(x => x.PerformedByRepId);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Representative>().WithMany().HasForeignKey(x => x.PerformedByRepId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.PerformedByUserId);
        b.HasIndex(x => x.PerformedByRepId);
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
        // Automation (2026-09-15): origin + adjusted flag + system-written details + the mirrored penalty.
        b.Property(x => x.Origin).HasDefaultValue(DeductionOrigin.Manual);
        b.Property(x => x.IsAdjusted).HasDefaultValue(false);
        b.Property(x => x.SystemNote).HasMaxLength(1000);
        b.Property(x => x.PenaltyRecordId);
        // Restrict: the penalty command removes its mirrored deduction first, so a dangling mirror can never remain.
        b.HasOne<PenaltyRecord>().WithMany().HasForeignKey(x => x.PenaltyRecordId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.PenaltyRecordId).IsUnique().HasFilter("penalty_record_id IS NOT NULL");
        // One automated Percentage Deal row per area per month (PeriodFrom is always the 1st for AutoDeal).
        b.HasIndex(x => new { x.AreaId, x.PeriodFrom }).IsUnique().HasFilter("origin = 'AutoDeal'").HasDatabaseName("ux_deduction_auto_deal_area_month");
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
        b.Ignore(x => x.RepIds); // derived from Shares
        // Per-rep shares as jsonb (a collection belongs to its reps; there is no lab). Like the former rep_ids list there is
        // no FK to representative (jsonb) — reps are never deleted, only deactivated.
        b.Property(x => x.Shares)
            .HasColumnName("rep_shares").HasColumnType("jsonb")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion<CollectionShareListConverter>(new CollectionShareListComparer());
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
