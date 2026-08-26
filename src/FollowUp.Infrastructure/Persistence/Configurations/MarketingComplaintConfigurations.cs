using FollowUp.Domain.Complaints;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal sealed class MarketingVisitConfiguration : IEntityTypeConfiguration<MarketingVisit>
{
    public void Configure(EntityTypeBuilder<MarketingVisit> b)
    {
        b.ToTable("marketing_visit");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Ignore(x => x.Reference); // derived from Number

        b.Property(x => x.Number);
        b.HasIndex(x => x.Number).IsUnique(); // sequential MV-n reference
        b.Property(x => x.ScheduledDate);
        b.Property(x => x.ScheduledTime);
        b.Property(x => x.Plan).HasMaxLength(2000);
        b.Property(x => x.Outcome).HasMaxLength(2000);
        b.Property(x => x.CancellationReason).HasMaxLength(500);
        b.Property(x => x.CompletedAt);

        b.HasOne<Laboratory>().WithMany().HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Representative>().WithMany().HasForeignKey(x => x.RepresentativeId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.LaboratoryId);
        b.HasIndex(x => new { x.Status, x.ScheduledDate }); // scheduled-first listings (BR-10)
    }
}

internal sealed class ComplaintConfiguration : IEntityTypeConfiguration<Complaint>
{
    public void Configure(EntityTypeBuilder<Complaint> b)
    {
        b.ToTable("complaint");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.Ignore(x => x.Reference); // derived from Number

        b.Property(x => x.Number);
        b.HasIndex(x => x.Number).IsUnique(); // gap-free sequential CMP-n (BR-2)
        b.Property(x => x.Category).HasMaxLength(100).IsRequired();
        b.Property(x => x.ViaChannel).HasMaxLength(100).IsRequired();
        b.Property(x => x.AssignedTeam).HasMaxLength(100);
        b.Property(x => x.Details).HasColumnType("text");
        b.Property(x => x.ResolvedBy).HasMaxLength(100);
        b.Property(x => x.ResolvedAt);
        b.Property(x => x.RepresentativeId);
        b.Property(x => x.ReceivedAt);
        b.Property(x => x.IsValid);
        b.Property(x => x.ValidityNotes).HasMaxLength(2000);
        b.Property(x => x.InvestigationNotes).HasColumnType("text");
        b.Property(x => x.OutcomeType).HasMaxLength(100);
        b.Property(x => x.OutcomeSummary).HasMaxLength(2000);
        b.Property(x => x.ResolutionSummary).HasMaxLength(2000);
        b.Property(x => x.CreatedAt);
        b.Property(x => x.CreatedBy).HasMaxLength(100).HasDefaultValue("system");
        b.Property(x => x.UpdatedAt);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);

        b.HasOne<Laboratory>().WithMany().HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.LaboratoryId);
        b.HasIndex(x => x.Status);
    }
}
