using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Outbox;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_message");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasMaxLength(200).IsRequired();
        b.Property(x => x.Content).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.OccurredOn);
        b.Property(x => x.ProcessedAt);
        b.Property(x => x.Error).HasColumnType("text");
        b.Property(x => x.Attempts);
        // Dispatcher polls for unprocessed rows in occurrence order.
        b.HasIndex(x => new { x.ProcessedAt, x.OccurredOn });
    }
}
