using FollowUp.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>Domain events are transient dispatch state, never persisted.</summary>
    public static void IgnoreDomainEvents<T>(this EntityTypeBuilder<T> b) where T : class =>
        b.Ignore(nameof(Entity<int>.DomainEvents));

    /// <summary>Maps the IAuditable provenance columns (distinct from the immutable audit trail).</summary>
    public static void MapAuditable<T>(this EntityTypeBuilder<T> b) where T : class, IAuditable
    {
        b.Property(x => x.CreatedAt);
        b.Property(x => x.CreatedBy).HasMaxLength(100).HasDefaultValue("system");
        b.Property(x => x.UpdatedAt);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
    }
}
