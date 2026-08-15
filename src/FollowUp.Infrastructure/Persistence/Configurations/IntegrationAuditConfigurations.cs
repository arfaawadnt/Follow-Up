using FollowUp.Domain.Audit;
using FollowUp.Domain.Integration;
using FollowUp.Domain.Signatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal sealed class OracleConfigConfiguration : IEntityTypeConfiguration<OracleConfig>
{
    public void Configure(EntityTypeBuilder<OracleConfig> b)
    {
        b.ToTable("oracle_config");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(50);
        b.IgnoreDomainEvents();

        b.Property(x => x.Enabled);
        b.Property(x => x.IntervalHours);
        b.Property(x => x.ConnectionString).HasColumnType("text"); // never returned by the API
        b.Property(x => x.LastStatus).HasMaxLength(500);
        b.Property(x => x.LastSyncAt);

        b.Property(x => x.Queries)
            .HasColumnName("queries")
            .HasColumnType("jsonb")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion<AllowListedQueryListConverter>(new AllowListedQueryListComparer());
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("audit_entry");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.OccurredAt);
        b.Property(x => x.Actor).HasMaxLength(100);
        b.Property(x => x.Entity).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.BeforeJson).HasColumnType("jsonb");
        b.Property(x => x.AfterJson).HasColumnType("jsonb");
        b.Property(x => x.CorrelationId).HasMaxLength(64);

        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.Entity, x.EntityId });
    }
}

internal sealed class ElectronicSignatureConfiguration : IEntityTypeConfiguration<ElectronicSignature>
{
    public void Configure(EntityTypeBuilder<ElectronicSignature> b)
    {
        b.ToTable("electronic_signature");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.Module).HasMaxLength(50).IsRequired();
        b.Property(x => x.RecordId).HasMaxLength(100).IsRequired();
        b.Property(x => x.RecordVersion);
        b.Property(x => x.SignerUsername).HasMaxLength(100);
        b.Property(x => x.AuthLevel).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.SignerIp).HasMaxLength(64);
        b.Property(x => x.SignedAt);

        b.HasIndex(x => new { x.Module, x.RecordId });
    }
}
