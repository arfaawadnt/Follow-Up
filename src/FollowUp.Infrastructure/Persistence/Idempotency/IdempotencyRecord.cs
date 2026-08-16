using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Idempotency;

/// <summary>
/// Records a processed command by its client idempotency key so a retry returns the first result instead of
/// executing again. Written in the same transaction as the command's effect (via TransactionBehavior).
/// </summary>
public sealed class IdempotencyRecord
{
    public string Key { get; init; } = null!;         // client-supplied Idempotency-Key
    public string RequestType { get; init; } = null!;
    public string? ResponseJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_record");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(200);
        b.Property(x => x.RequestType).HasMaxLength(200);
        b.Property(x => x.ResponseJson).HasColumnType("jsonb");
        b.Property(x => x.CreatedAt);
    }
}
