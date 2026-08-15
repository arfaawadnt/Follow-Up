using FollowUp.Domain.Identity;
using FollowUp.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> b)
    {
        b.ToTable("notification_template");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.EventKey).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.EventKey).IsUnique();
        b.Property(x => x.SubjectEn).HasMaxLength(300);
        b.Property(x => x.SubjectAr).HasMaxLength(300);
        b.Property(x => x.BodyEn).HasColumnType("text");
        b.Property(x => x.BodyAr).HasColumnType("text");
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable("notification_preference");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.EventKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.System);
        b.Property(x => x.Mail);
        b.Property(x => x.WhatsApp);

        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.UserId, x.EventKey }).IsUnique();
    }
}

internal sealed class SystemNotificationConfiguration : IEntityTypeConfiguration<SystemNotification>
{
    public void Configure(EntityTypeBuilder<SystemNotification> b)
    {
        b.ToTable("system_notification");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();
        b.Ignore(x => x.IsRead);

        b.Property(x => x.EventKey).HasMaxLength(100);
        b.Property(x => x.Title).HasMaxLength(300);
        b.Property(x => x.Body).HasColumnType("text");
        b.Property(x => x.CreatedAt);
        b.Property(x => x.ReadAt);

        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.RecipientUserId, x.ReadAt });
    }
}

internal sealed class NotificationDeliveryLogConfiguration : IEntityTypeConfiguration<NotificationDeliveryLog>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryLog> b)
    {
        b.ToTable("notification_delivery_log");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.Recipient).HasMaxLength(200);
        b.Property(x => x.EventKey).HasMaxLength(100);
        b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.Attempts);
        b.Property(x => x.LastError).HasColumnType("text");
        b.Property(x => x.QueuedAt);
        b.Property(x => x.LastAttemptAt);
        b.HasIndex(x => x.Status);
    }
}
