using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal sealed class DailyVisitConfiguration : IEntityTypeConfiguration<DailyVisit>
{
    public void Configure(EntityTypeBuilder<DailyVisit> b)
    {
        b.ToTable("daily_visit");
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid"); // xmin optimistic concurrency (BRD-2)
        b.IgnoreDomainEvents();
        b.Ignore(x => x.RollsToMonthly);
        b.MapAuditable();

        b.Property(x => x.VisitDate);
        b.Property(x => x.ScheduledTime);
        b.Property(x => x.SampleCount);
        b.Property(x => x.CheckedInBy).HasMaxLength(100);
        b.Property(x => x.AdminChecked);
        b.Property(x => x.TotalRequired);
        b.Property(x => x.RequestCount);
        b.Property(x => x.OutsourceCount);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.OwnsOne(x => x.Transfer, t =>
        {
            t.Property(d => d.DriverName).HasColumnName("driver_name").HasMaxLength(200);
            t.Property(d => d.DriverMobile).HasColumnName("driver_mobile").HasMaxLength(40);
            t.Property(d => d.CarPlate).HasColumnName("car_plate").HasMaxLength(40);
        });

        b.HasOne<Laboratory>().WithMany().HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Representative>().WithMany().HasForeignKey(x => x.CollectorRepId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Representative>().WithMany().HasForeignKey(x => x.TransferRepId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.VisitDate);
        b.HasIndex(x => new { x.LaboratoryId, x.VisitDate });
        b.HasIndex(x => x.Status);
        // A lab has at most one visit per scheduled time per day: a DB-level second line of defense (BRD-9)
        // against the two-Hangfire-job race (rollover vs intra-day reconcile) that app-level skip checks miss.
        b.HasIndex(x => new { x.LaboratoryId, x.VisitDate, x.ScheduledTime }).IsUnique();
    }
}

internal sealed class VisitHistoryConfiguration : IEntityTypeConfiguration<VisitHistory>
{
    public void Configure(EntityTypeBuilder<VisitHistory> b)
    {
        b.ToTable("visit_history");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.VisitDate);
        b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.SampleCount);
        b.Property(x => x.AdminChecked);
        b.Property(x => x.ArchivedAt);

        // Lifecycle-stage snapshot (FR-8 report; nullable on rows archived before these existed).
        b.Property(x => x.ScheduledTime);
        b.Property(x => x.CheckedInAt);
        b.Property(x => x.TransferConfirmedAt);
        b.Property(x => x.ReceivedAt);

        // Transfer-leg snapshot for the motion tracking report (driver + transfer rep).
        b.Property(x => x.TransferRepId);
        b.Property(x => x.DriverName).HasMaxLength(200);
        b.Property(x => x.DriverMobile).HasMaxLength(40);
        b.Property(x => x.CarPlate).HasMaxLength(40);

        // RESTRICT: permanent archive cannot be silently removed (SRS data rules).
        b.HasOne<Laboratory>().WithMany().HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.VisitDate);
        b.HasIndex(x => new { x.LaboratoryId, x.VisitDate });
    }
}

internal sealed class OutsourceSampleConfiguration : IEntityTypeConfiguration<OutsourceSample>
{
    public void Configure(EntityTypeBuilder<OutsourceSample> b)
    {
        b.ToTable("outsource_sample");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.VisitDate);
        b.Property(x => x.DestinationLab).HasMaxLength(200);
        b.Property(x => x.Quantity);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne<Laboratory>().WithMany().HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Cascade);
        // Unique per (visit_date, lab) — SRS FR-9.
        b.HasIndex(x => new { x.VisitDate, x.LaboratoryId }).IsUnique();
    }
}

internal sealed class SampleTrackingConfiguration : IEntityTypeConfiguration<SampleTracking>
{
    public void Configure(EntityTypeBuilder<SampleTracking> b)
    {
        b.ToTable("sample_tracking");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.Ignore(x => x.IsComplete);
        b.Ignore(x => x.IsUntouched);
        b.MapAuditable();

        b.Property(x => x.Area).HasMaxLength(100).IsRequired();
        b.Property(x => x.Date);
        b.Property(x => x.Count);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.OwnsOne(x => x.DataEntry, s => { s.Property(t => t.User).HasColumnName("data_entry_by").HasMaxLength(100); s.Property(t => t.At).HasColumnName("data_entry_at"); });
        b.OwnsOne(x => x.Review, s => { s.Property(t => t.User).HasColumnName("review_by").HasMaxLength(100); s.Property(t => t.At).HasColumnName("review_at"); });
        b.OwnsOne(x => x.Sort, s => { s.Property(t => t.User).HasColumnName("sort_by").HasMaxLength(100); s.Property(t => t.At).HasColumnName("sort_at"); });

        b.HasIndex(x => new { x.Area, x.Date }).IsUnique();
    }
}
