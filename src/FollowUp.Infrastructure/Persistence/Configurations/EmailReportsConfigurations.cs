using FollowUp.Domain.Emailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal sealed class SmtpConfigConfiguration : IEntityTypeConfiguration<SmtpConfig>
{
    public void Configure(EntityTypeBuilder<SmtpConfig> b)
    {
        b.ToTable("smtp_config");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(50);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.Enabled);
        b.Property(x => x.Host).HasMaxLength(255);
        b.Property(x => x.Port);
        b.Property(x => x.UseSsl);
        b.Property(x => x.FromAddress).HasMaxLength(255);
        b.Property(x => x.User).HasMaxLength(255);
        b.Property(x => x.Password).HasColumnType("text"); // secret — masked by the API
        b.Ignore(x => x.HasPassword);
    }
}

internal sealed class StatsEmailSubscriptionConfiguration : IEntityTypeConfiguration<StatsEmailSubscription>
{
    public void Configure(EntityTypeBuilder<StatsEmailSubscription> b)
    {
        b.ToTable("stats_email_subscription");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion<StatsEmailSubscriptionIdConverter>();
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.IncludeLabStats);
        b.Property(x => x.IncludeTestStats);
        b.Property(x => x.IncludeAreaStats);
        b.Property(x => x.FiltersJson).HasColumnType("jsonb");
        b.Property(x => x.SendHour);
        b.Property(x => x.SendMinute);
        b.Property(x => x.WindowDays);
        b.Property(x => x.Enabled);
        b.Property(x => x.LastRunAt);
        b.Property(x => x.LastStatus).HasMaxLength(500);

        b.Property(x => x.UserIds)
            .HasColumnName("user_ids").HasColumnType("jsonb")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion<GuidListConverter>(new GuidListComparer());
        b.Property(x => x.Emails)
            .HasColumnName("emails").HasColumnType("jsonb")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion<StringListConverter>(new StringListComparer());
    }
}
