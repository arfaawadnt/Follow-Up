using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal sealed class LaboratoryConfiguration : IEntityTypeConfiguration<Laboratory>
{
    public void Configure(EntityTypeBuilder<Laboratory> b)
    {
        b.ToTable("laboratory");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.Code).HasConversion<LabCodeConverter>().HasMaxLength(32).IsRequired();
        // Case-insensitive uniqueness (BR-1) — the code is already normalized upper-case.
        b.HasIndex(x => x.Code).IsUnique();

        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Segment).HasMaxLength(32).IsRequired();
        b.Property(x => x.Branch).HasMaxLength(100);
        b.Property(x => x.Governorate).HasMaxLength(100);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.Area).HasMaxLength(100);
        b.Property(x => x.Category).HasMaxLength(100);
        b.Property(x => x.Payer).HasMaxLength(100);
        b.Property(x => x.ContractType).HasMaxLength(100);
        b.Property(x => x.LicenseNo).HasMaxLength(100);
        b.Property(x => x.LicenseDate);
        b.Property(x => x.AvgMonthlySamples);
        b.Property(x => x.PreferredChannel).HasMaxLength(40);
        b.Property(x => x.LoyaltyTier).HasMaxLength(32);
        b.Property(x => x.MonthlyTarget);
        b.Property(x => x.LoyaltyPoints);

        b.Property(x => x.Schedule)
            .HasColumnName("schedule")
            .HasConversion<VisitScheduleConverter>(new VisitScheduleComparer())
            .HasColumnType("jsonb")
            .IsRequired();

        b.OwnsOne(x => x.Location, loc =>
        {
            loc.Property(l => l.Latitude).HasColumnName("latitude");
            loc.Property(l => l.Longitude).HasColumnName("longitude");
        });

        // Optimistic concurrency (FR-3) via Postgres xmin.
        b.Property(x => x.RowVersion).IsRowVersion();

        // Collectors — multiple per lab (matches the reference), stored as a jsonb id array (like Area.TransferReps).
        b.Property(x => x.CollectorRepIds)
            .HasColumnName("collector_reps")
            .HasColumnType("jsonb")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion<RepIdListConverter>(new RepIdListComparer());

        // Marketing rep — single, RESTRICT so an assigned rep can't be silently removed (BR-4).
        b.HasOne<Representative>().WithMany().HasForeignKey(x => x.MarketingRepId).OnDelete(DeleteBehavior.Restrict);

        // Contacts — child entities inside the aggregate (CASCADE).
        b.OwnsMany(x => x.Contacts, c =>
        {
            c.ToTable("contact_person");
            c.WithOwner().HasForeignKey("laboratory_id");
            c.HasKey(x => x.Id);
            c.Property(x => x.Name).HasMaxLength(200).IsRequired();
            c.Property(x => x.Role).HasConversion<int>();
            c.Property(x => x.Phone).HasMaxLength(40);
            c.Property(x => x.Birthday);
            c.HasIndex("laboratory_id");
        });
        b.Navigation(x => x.Contacts).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.Governorate);
    }
}

internal sealed class RepresentativeConfiguration : IEntityTypeConfiguration<Representative>
{
    public void Configure(EntityTypeBuilder<Representative> b)
    {
        b.ToTable("representative");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.Property(x => x.GoalType).HasMaxLength(100);
        b.Property(x => x.Metric).HasMaxLength(100);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Branch).HasMaxLength(100);
        b.Property(x => x.Governorate).HasMaxLength(100);
        b.Property(x => x.Area).HasMaxLength(100);
        b.Property(x => x.EmploymentType).HasMaxLength(40);
        b.Property(x => x.IsActive).HasDefaultValue(true);

        b.Property(x => x.RowVersion).IsRowVersion();
    }
}
