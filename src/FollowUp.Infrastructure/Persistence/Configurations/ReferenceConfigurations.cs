using FollowUp.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal sealed class RefItemConfiguration : IEntityTypeConfiguration<RefItem>
{
    public void Configure(EntityTypeBuilder<RefItem> b)
    {
        b.ToTable("ref_item");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.Code).HasMaxLength(64).IsRequired();
        b.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(200);
        b.Property(x => x.SortOrder);
        b.Property(x => x.Source).HasDefaultValue(FollowUp.Domain.Common.RecordSource.Manual);
        b.HasIndex(x => new { x.Type, x.Code }).IsUnique();
    }
}

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> b)
    {
        b.ToTable("city");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Governorate).HasMaxLength(100).IsRequired();
        b.Property(x => x.SourceCode).HasMaxLength(64);
        b.Property(x => x.Source).HasDefaultValue(FollowUp.Domain.Common.RecordSource.Manual);
        b.HasIndex(x => new { x.Governorate, x.Name });
        b.HasIndex(x => x.SourceCode);
    }
}

internal sealed class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> b)
    {
        b.ToTable("area");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.TransportationRequired);
        b.Property(x => x.SourceCode).HasMaxLength(64);
        b.Property(x => x.Source).HasDefaultValue(FollowUp.Domain.Common.RecordSource.Manual);
        b.HasIndex(x => x.SourceCode);

        b.Property(x => x.TransferReps)
            .HasColumnName("transfer_reps")
            .HasColumnType("jsonb")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion<RepIdListConverter>(new RepIdListComparer());

        b.HasOne<City>().WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.CityId);
    }
}

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.ToTable("app_setting");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("key").HasMaxLength(200);
        b.Ignore(x => x.Key); // alias for Id
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.Value).HasColumnType("text");
        b.Property(x => x.IsSecret);
    }
}
